using Avalonia.Threading;
using EasyChat.Contracts.Input;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Selection;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Shortcuts;
using EasyChat.Contracts.TextAssist;
using EasyChat.Contracts.Translation;
using EasyChat.Presentation.Features.Input;
using EasyChat.Presentation.Features.SelectionTranslation;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Features.TextAssist;
using EasyChat.Presentation.Features.Translation;
using EasyChat.Presentation.Foundation.UiHost;
using Microsoft.Extensions.Logging;

namespace EasyChat.Presentation.Features.Shortcuts;

public sealed class InputTranslateShortcutAction(
    IWindowFocus focus,
    ISelectedTextUseCases selectedText,
    IInputTranslationUseCases inputTranslation,
    ITypingWindowFactory typingWindow,
    ILogger<InputTranslateShortcutAction> logger) : IShortcutAction
{
    public string ActionType => "InputTranslate";
    public bool PreventConcurrentExecution => true;

    public async ValueTask ExecuteAsync(
        ShortcutParameterSettings? parameter,
        CancellationToken cancellationToken = default)
    {
        var target = await focus.GetForegroundTargetAsync(cancellationToken).ConfigureAwait(false);
        if (target.IsFailure)
        {
            logger.LogWarning("Unable to resolve input translation target: {Error}", target.Error.Message);
            return;
        }

        if (parameter?.ReplaceCurrentInput != true)
        {
            typingWindow.Show(target.Value, parameter);
            return;
        }

        var captured = await selectedText.CaptureAsync(
            new SelectedTextCaptureCommand(
                SelectedTextCaptureMode.All,
                ExpectedForegroundTarget: target.Value),
            cancellationToken).ConfigureAwait(false);
        if (captured.IsFailure)
        {
            logger.LogWarning("Unable to read current input: {Error}", captured.Error.Message);
            return;
        }

        var delivered = await inputTranslation.TranslateAndDeliverAsync(
            new InputTranslationRequest(
                captured.Value.Text,
                target.Value,
                ReplaceCurrentInput: true,
                BeforeKey: parameter.InputTranslateBeforeKey,
                AfterKey: parameter.InputTranslateAfterKey),
            cancellationToken).ConfigureAwait(false);
        if (delivered.IsFailure)
            logger.LogWarning("Input translation failed: {Error}", delivered.Error.Message);
    }
}

public sealed class QuickTextAssistShortcutAction(
    string actionType,
    bool correction,
    ISelectedTextUseCases selectedText,
    ITextAssistWindowCoordinator windows,
    ILogger<QuickTextAssistShortcutAction> logger) : IShortcutAction
{
    public string ActionType { get; } = actionType;
    public bool PreventConcurrentExecution => true;

    public async ValueTask ExecuteAsync(
        ShortcutParameterSettings? parameter,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (await windows.CloseEditorIfOpenAsync(cancellationToken))
                return;

            var text = string.Empty;
            if (parameter?.ReadSelectedText ?? true)
            {
                var captured = await selectedText.CaptureAsync(
                    new SelectedTextCaptureCommand(SelectedTextCaptureMode.Copy),
                    cancellationToken).ConfigureAwait(false);
                if (captured.IsSuccess)
                    text = captured.Value.Text;
                else
                    logger.LogDebug("Unable to capture selected text: {Error}", captured.Error.Message);
            }

            await windows.ShowEditorAsync(text, correction, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to open {ActionType}.", ActionType);
        }
    }
}

public sealed class SelectionTranslateShortcutAction(
    ISelectedTextUseCases selectedText,
    ISelectionInteractionSink selectionSink,
    ITranslationWindowCoordinator translationWindow,
    IPointerPosition pointer,
    SettingsSession settings,
    ILogger<SelectionTranslateShortcutAction> logger) : IShortcutAction
{
    public string ActionType => "SelectionTranslate";
    public bool PreventConcurrentExecution => true;

    public async ValueTask ExecuteAsync(
        ShortcutParameterSettings? parameter,
        CancellationToken cancellationToken = default)
    {
        var captured = await selectedText.CaptureAsync(
            new SelectedTextCaptureCommand(SelectedTextCaptureMode.Automatic),
            cancellationToken).ConfigureAwait(false);
        if (captured.IsFailure || string.IsNullOrWhiteSpace(captured.Value.Text))
        {
            if (captured.IsFailure)
                logger.LogWarning("Unable to capture selected text: {Error}", captured.Error.Message);
            return;
        }

        var anchor = captured.Value.PointerPosition ?? pointer.GetCurrent();
        if (parameter?.ShowSelectionToolbar == true)
        {
            var toolbar = settings.SelectionTranslation;
            await selectionSink.OnSelectionCapturedAsync(
                new SelectionCapture(
                    captured.Value,
                    SelectionGesture.Drag,
                    new SelectionToolbarOptions(
                        toolbar.TranslationEnabled,
                        toolbar.CorrectionEnabled,
                        toolbar.PolishEnabled,
                        toolbar.SummaryEnabled)),
                cancellationToken);
            return;
        }

        await translationWindow.ShowSentenceAsync(
            captured.Value.Text,
            anchor,
            showCloseButton: true,
            cancellationToken);
    }
}

public sealed class SwitchTranslationProfileShortcutAction(
    SettingsSession settings,
    IUiToastHost toasts,
    ILogger<SwitchTranslationProfileShortcutAction> logger) : IShortcutAction
{
    public string ActionType => "SwitchEngineSourceTarget";
    public bool PreventConcurrentExecution => false;

    public ValueTask ExecuteAsync(
        ShortcutParameterSettings? parameter,
        CancellationToken cancellationToken = default) =>
        OnUiAsync(() => Execute(parameter), cancellationToken);

    private void Execute(ShortcutParameterSettings? parameter)
    {
        if (parameter?.Source is null || parameter.Target is null)
        {
            Show("Error", "Invalid parameter. Source or Target is missing.");
            return;
        }

        var machine = ResolveMachine(parameter.EngineId, parameter.Engine);
        if (machine is not null)
        {
            if (!Supports(parameter.Source, machine.Value.Name)
                || !Supports(parameter.Target, machine.Value.Name))
            {
                Show(
                    "Error",
                    $"Engine {machine.Value.Name} does not support language " +
                    $"{parameter.Source.DisplayName} or {parameter.Target.DisplayName}");
                return;
            }

            settings.General.UsingMachineTransId = machine.Value.Id;
            settings.General.UsingMachineTrans = machine.Value.Name;
            settings.General.SourceLanguage = parameter.Source;
            settings.General.TargetLanguage = parameter.Target;
            settings.General.TransEngine = TranslationEngineNames.MachineTrans;
            Show(
                "Engine & Language Switched",
                $"Switched to {machine.Value.Name} (Machine)\n" +
                $"Source: {parameter.Source.DisplayName}\nTarget: {parameter.Target.DisplayName}");
            return;
        }

        var model = settings.AiModel.ConfiguredModels.FirstOrDefault(candidate =>
                        !string.IsNullOrWhiteSpace(parameter.EngineId)
                        && candidate.Id == parameter.EngineId)
                    ?? settings.AiModel.ConfiguredModels.FirstOrDefault(candidate =>
                        candidate.Name == parameter.Engine);
        if (model is null)
        {
            Show("Error", $"Unknown engine: {parameter.Engine}");
            return;
        }

        settings.General.UsingAiModel = model.Name;
        settings.General.UsingAiModelId = model.Id;
        settings.General.SourceLanguage = parameter.Source;
        settings.General.TargetLanguage = parameter.Target;
        settings.General.TransEngine = TranslationEngineNames.AiModel;
        Show(
            "Engine & Language Switched",
            $"Switched to {model.Name} (AI)\n" +
            $"Source: {parameter.Source.DisplayName}\nTarget: {parameter.Target.DisplayName}");
        logger.LogInformation("Translation profile switched to {Engine} ({EngineId}).", model.Name, model.Id);
    }

    private (string Id, string Name)? ResolveMachine(string? id, string? name)
    {
        var candidates = new[]
        {
            (settings.MachineTranslation.Baidu.Id, MachineTranslationProviderNames.Baidu),
            (settings.MachineTranslation.Tencent.Id, MachineTranslationProviderNames.Tencent),
            (settings.MachineTranslation.Google.Id, MachineTranslationProviderNames.Google),
            (settings.MachineTranslation.DeepL.Id, MachineTranslationProviderNames.DeepL)
        };
        return candidates.Cast<(string Id, string Name)?>().FirstOrDefault(candidate =>
            candidate is { } value
            && ((!string.IsNullOrWhiteSpace(id)
                 && (value.Id == id || value.Name.Equals(id, StringComparison.OrdinalIgnoreCase)))
                || value.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool Supports(LanguageSettings language, string provider) =>
        language.ProviderCodes.TryGetValue(provider, out var code)
        && !string.IsNullOrWhiteSpace(code);

    private void Show(string title, string message) => toasts.Show(title, message);

    private static async ValueTask OnUiAsync(
        Action action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }
        await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken);
    }
}
