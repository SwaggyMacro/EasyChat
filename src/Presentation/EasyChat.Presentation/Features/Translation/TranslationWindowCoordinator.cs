using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.SelectionTranslation;
using EasyChat.Contracts.Speech;
using EasyChat.Contracts.Translation;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Features.Translation.Views;
using EasyChat.Presentation.Foundation.Localization;
using EasyChat.Presentation.Foundation.Platform;
using Microsoft.Extensions.Logging;

namespace EasyChat.Presentation.Features.Translation;

public interface ITranslationWindowCoordinator
{
    ValueTask PrewarmAsync(CancellationToken cancellationToken = default);

    ValueTask ShowSentenceAsync(
        string text,
        PhysicalScreenPoint? anchor = null,
        bool showCloseButton = true,
        CancellationToken cancellationToken = default);

    ValueTask ShowDictionaryAsync(
        string text,
        string sourceLanguageId,
        string targetLanguageId,
        bool centerOnScreen = false,
        PhysicalScreenPoint? anchor = null,
        CancellationToken cancellationToken = default);

    ValueTask<bool> ContainsAsync(
        PhysicalScreenPoint point,
        CancellationToken cancellationToken = default);

    ValueTask<bool> IsVisibleAsync(CancellationToken cancellationToken = default);

    ValueTask CloseAsync(CancellationToken cancellationToken = default);
}

public sealed class TranslationWindowCoordinator(
    ISelectionTranslationUseCases translation,
    ITranslationLanguageCatalog languages,
    ITtsUseCases tts,
    SettingsSession settings,
    IPlatformWindowBehavior platformWindowBehavior,
    ILoggerFactory loggerFactory) : ITranslationWindowCoordinator
{
    private TranslationDictionaryWindowView? _current;
    private TranslationWindowSession? _prewarmed;

    public ValueTask PrewarmAsync(CancellationToken cancellationToken = default) =>
        OnUiAsync(() =>
        {
            _prewarmed ??= CreateWindow();
        }, cancellationToken);

    public async ValueTask ShowSentenceAsync(
        string text,
        PhysicalScreenPoint? anchor = null,
        bool showCloseButton = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var window = await ShowShellAsync(anchor, centerOnScreen: false, showCloseButton, cancellationToken);
        await window.ViewModel.InitializeAsync(text);
        if (anchor is { } point)
            await OnUiAsync(() => PositionNearIfUnadjusted(window.View, point), cancellationToken);
    }

    public async ValueTask ShowDictionaryAsync(
        string text,
        string sourceLanguageId,
        string targetLanguageId,
        bool centerOnScreen = false,
        PhysicalScreenPoint? anchor = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var window = await ShowShellAsync(anchor, centerOnScreen, showCloseButton: true, cancellationToken);
        await window.ViewModel.InitializeDictionaryAsync(text, sourceLanguageId, targetLanguageId);
        if (!centerOnScreen && anchor is { } point)
            await OnUiAsync(() => PositionNearIfUnadjusted(window.View, point), cancellationToken);
    }

    public ValueTask<bool> ContainsAsync(
        PhysicalScreenPoint point,
        CancellationToken cancellationToken = default) =>
        OnUiAsync(() =>
        {
            if (_current?.IsVisible != true)
                return false;
            var clientPoint = _current.PointToClient(new PixelPoint(point.X, point.Y));
            return new Rect(_current.Bounds.Size).Contains(clientPoint);
        }, cancellationToken);

    public ValueTask<bool> IsVisibleAsync(CancellationToken cancellationToken = default) =>
        OnUiAsync(() => _current?.IsVisible == true, cancellationToken);

    public ValueTask CloseAsync(CancellationToken cancellationToken = default) =>
        OnUiAsync(() =>
        {
            _current?.Close();
            _current = null;
        }, cancellationToken);

    private async ValueTask<TranslationWindowSession> ShowShellAsync(
        PhysicalScreenPoint? anchor,
        bool centerOnScreen,
        bool showCloseButton,
        CancellationToken cancellationToken)
    {
        return await OnUiAsync(() =>
        {
            _current?.Close();
            var prepared = _prewarmed ?? CreateWindow();
            _prewarmed = null;
            _current = prepared.View;
            prepared.ViewModel.ShowCloseButton = showCloseButton;
            prepared.View.Closed += OnCurrentClosed;

            if (centerOnScreen)
            {
                prepared.View.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            else if (anchor is { } point)
            {
                PositionNear(prepared.View, point);
            }

            prepared.View.Show();
            return prepared;
        }, cancellationToken);
    }

    private TranslationWindowSession CreateWindow()
    {
        var viewModel = new TranslationDictionaryWindowViewModel(translation, languages, tts, settings);
        var view = new TranslationDictionaryWindowView(
            viewModel,
            platformWindowBehavior,
            loggerFactory.CreateLogger<TranslationDictionaryWindowView>());
        return new TranslationWindowSession(view, viewModel);
    }

    private void OnCurrentClosed(object? sender, EventArgs args)
    {
        if (ReferenceEquals(_current, sender))
            _current = null;
        Dispatcher.UIThread.Post(
            () => _prewarmed ??= CreateWindow(),
            DispatcherPriority.Background);
    }

    private static void PositionNear(Window window, PhysicalScreenPoint point)
    {
        var screen = window.Screens.ScreenFromPoint(new PixelPoint(point.X, point.Y)) ?? window.Screens.Primary;
        if (screen is null)
        {
            window.Position = new PixelPoint(point.X + 20, point.Y + 20);
            return;
        }

        var logicalWidth = window.Bounds.Width > 0 ? window.Bounds.Width : window.Width;
        // Prefer explicit Height (manual sizing) so placement does not jump as content streams.
        var logicalHeight = window.Bounds.Height > 1
            ? window.Bounds.Height
            : (double.IsNaN(window.Height) || window.Height <= 0 ? 480 : window.Height);
        window.Position = TranslationWindowPlacement.Near(
            screen.WorkingArea,
            screen.Scaling,
            point,
            logicalWidth,
            logicalHeight,
            logicalOffset: 20);
    }

    private static void PositionNearIfUnadjusted(
        TranslationDictionaryWindowView window,
        PhysicalScreenPoint point)
    {
        if (!window.HasUserAdjustedBounds)
            PositionNear(window, point);
    }

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

    private static async ValueTask<T> OnUiAsync<T>(
        Func<T> action,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Dispatcher.UIThread.CheckAccess())
            return action();
        return await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken);
    }

    private sealed record TranslationWindowSession(
        TranslationDictionaryWindowView View,
        TranslationDictionaryWindowViewModel ViewModel);
}
