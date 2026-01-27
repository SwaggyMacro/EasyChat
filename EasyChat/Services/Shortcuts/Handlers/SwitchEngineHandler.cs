using System.Linq;
using Avalonia.Threading;
using EasyChat.Constants;
using EasyChat.Models.Configuration;
using EasyChat.Services.Abstractions;
using EasyChat.Services.Languages;
using SukiUI.Toasts;

namespace EasyChat.Services.Shortcuts.Handlers;

/// <summary>
/// Handler for the SwitchEngineSourceTarget shortcut action.
/// Switches the translation engine and source/target languages.
/// </summary>
public class SwitchEngineHandler : IShortcutActionHandler
{
    private readonly IConfigurationService _configurationService;
    private readonly ISukiToastManager _toastManager;

    public string ActionType => "SwitchEngineSourceTarget";

    public SwitchEngineHandler(
        IConfigurationService configurationService,
        ISukiToastManager toastManager)
    {
        _configurationService = configurationService;
        _toastManager = toastManager;
    }

    public void Execute(ShortcutParameter? parameter = null)
    {
        if (parameter == null)
        {
            ShowError("Invalid parameter. Parameter is null.");
            return;
        }

        Dispatcher.UIThread.Post(() => ExecuteSwitch(parameter));
    }

    private void ExecuteSwitch(ShortcutParameter parameter)
    {
        if (parameter.Source == null || parameter.Target == null)
        {
            ShowError("Invalid parameter. Source or Target is missing.");
            return;
        }

        string? engineId = parameter.EngineId;
        string engineName = parameter.Engine;
        var sourceLang = parameter.Source;
        var targetLang = parameter.Target;

        // 1. Try to find by ID first (if provided)
        if (!string.IsNullOrEmpty(engineId))
        {
            var aiModelById = _configurationService.AiModel?.ConfiguredModels
                .FirstOrDefault(x => x.Id == engineId);
            if (aiModelById != null)
            {
                SwitchToAiModel(aiModelById, sourceLang, targetLang);
                return;
            }

            // Check if ID is a Machine provider name
            if (MachineTrans.SupportedProviders.Contains(engineId))
            {
                SwitchToMachineTrans(engineId, sourceLang, targetLang);
                return;
            }
        }

        // 2. Fallback to Name (Legacy behavior)
        if (MachineTrans.SupportedProviders.Contains(engineName))
        {
            SwitchToMachineTrans(engineName, sourceLang, targetLang);
            return;
        }

        var aiModelByName = _configurationService.AiModel?.ConfiguredModels
            .FirstOrDefault(x => x.Name == engineName);
        if (aiModelByName != null)
        {
            SwitchToAiModel(aiModelByName, sourceLang, targetLang);
            return;
        }

        ShowError($"Unknown engine: {engineName}");
    }

    private void SwitchToMachineTrans(string providerName, LanguageDefinition source, LanguageDefinition target)
    {
        var sourceCode = source.GetCode(providerName);
        var targetCode = target.GetCode(providerName);

        if (string.IsNullOrEmpty(sourceCode) || string.IsNullOrEmpty(targetCode))
        {
            ShowError($"Engine {providerName} does not support language {source.DisplayName} or {target.DisplayName}");
            return;
        }

        // Set specific engine FIRST, then engine type.
        // This ensures proper state even if TransEngine value doesn't change (RaiseAndSetIfChanged optimization).

        if (_configurationService.General != null)
        {

            _configurationService.General.UsingMachineTrans = providerName;
            _configurationService.General.SourceLanguage = source;
            _configurationService.General.TargetLanguage = target;
            _configurationService.General.TransEngine = Constant.TransEngineType.Machine;
        }

        ShowSuccess($"Switched to {providerName} (Machine)\nSource: {source.DisplayName}\nTarget: {target.DisplayName}");
    }

    private void SwitchToAiModel(CustomAiModel model, LanguageDefinition source, LanguageDefinition target)
    {
        // Set specific engine FIRST, then engine type.
        // This ensures proper state even if TransEngine value doesn't change (RaiseAndSetIfChanged optimization).
        if (_configurationService.General != null)
        {
            _configurationService.General.UsingAiModel = model.Name;
            _configurationService.General.UsingAiModelId = model.Id;
            _configurationService.General.SourceLanguage = source;
            _configurationService.General.TargetLanguage = target;
            _configurationService.General.TransEngine = Constant.TransEngineType.Ai;
        }

        ShowSuccess($"Switched to {model.Name} (AI)\nSource: {source.DisplayName}\nTarget: {target.DisplayName}");
    }

    private void ShowError(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _toastManager.CreateSimpleInfoToast()
                .WithTitle("Error")
                .WithContent(message)
                .Queue();
        });
    }

    private void ShowSuccess(string message)
    {
        _toastManager.CreateSimpleInfoToast()
            .WithTitle("Engine & Language Switched")
            .WithContent(message)
            .Queue();
    }
}
