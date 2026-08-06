using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Selection;
using EasyChat.Contracts.TextAssist;
using EasyChat.Presentation.Features.SelectionTranslation.Views;
using EasyChat.Presentation.Features.TextAssist;
using EasyChat.Presentation.Features.Translation;
using EasyChat.Presentation.Foundation.Platform;
using Microsoft.Extensions.Logging;

namespace EasyChat.Presentation.Features.SelectionTranslation;

public sealed class SelectionInteractionSink(
    IPointerPosition pointerPosition,
    ITranslationWindowCoordinator translation,
    ITextAssistWindowCoordinator textAssist,
    IPlatformWindowBehavior platformWindowBehavior,
    ILoggerFactory loggerFactory) : ISelectionInteractionSink
{
    private readonly ILogger<SelectionInteractionSink> _logger = loggerFactory.CreateLogger<SelectionInteractionSink>();
    private SelectionIconWindowView? _toolbar;
    private SelectionCapture? _capture;

    public async ValueTask<SelectionSurfaceState> InspectSurfaceAsync(
        PhysicalScreenPoint point,
        CancellationToken cancellationToken = default)
    {
        var overToolbar = await OnUiAsync(() => Contains(_toolbar, point), cancellationToken);
        if (overToolbar) return new SelectionSurfaceState(true, true);
        if (await translation.ContainsAsync(point, cancellationToken))
            return new SelectionSurfaceState(true, true);
        if (await textAssist.ContainsResultAsync(point, cancellationToken))
            return new SelectionSurfaceState(true, true);
        if (await translation.IsVisibleAsync(cancellationToken)
            || await textAssist.IsResultVisibleAsync(cancellationToken))
            return new SelectionSurfaceState(false, true);
        return new SelectionSurfaceState(false, false);
    }

    public async ValueTask OnMonitoringStartedAsync(CancellationToken cancellationToken = default)
    {
        await OnUiAsync(() =>
        {
            var toolbar = EnsureToolbar();
            if (toolbar.IsVisible) return;
            var opacity = toolbar.Opacity;
            toolbar.Opacity = 0;
            toolbar.Position = new PixelPoint(-10000, -10000);
            toolbar.Show();
            toolbar.Hide();
            toolbar.Opacity = opacity;
        }, cancellationToken);
        await translation.PrewarmAsync(cancellationToken);
    }

    public async ValueTask OnExternalPointerPressedAsync(
        PhysicalScreenPoint point,
        CancellationToken cancellationToken = default)
    {
        await OnUiAsync(() => _toolbar?.Hide(), cancellationToken);
        await translation.CloseAsync(cancellationToken);
        await textAssist.CloseResultAsync(cancellationToken);
    }

    public ValueTask OnSelectionCapturedAsync(
        SelectionCapture capture,
        CancellationToken cancellationToken = default) =>
        OnUiAsync(() =>
        {
            _capture = capture;
            var toolbar = EnsureToolbar();
            toolbar.Configure(capture.Toolbar);
            toolbar.HideLoading();
            PositionToolbar(toolbar, capture.SelectedText.PointerPosition ?? pointerPosition.GetCurrent());
            toolbar.Show();
            toolbar.Topmost = true;
        }, cancellationToken);

    private SelectionIconWindowView EnsureToolbar()
    {
        if (_toolbar is not null) return _toolbar;
        _toolbar = new SelectionIconWindowView(
            platformWindowBehavior,
            loggerFactory.CreateLogger<SelectionIconWindowView>());
        _toolbar.TranslateClicked += (_, _) => Run(TextAssistOperation.Translation);
        _toolbar.CorrectionClicked += (_, _) => Run(TextAssistOperation.Correction);
        _toolbar.PolishClicked += (_, _) => Run(TextAssistOperation.Polish);
        _toolbar.SummaryClicked += (_, _) => Run(TextAssistOperation.Summary);
        _toolbar.ExplanationClicked += (_, _) => Run(TextAssistOperation.Explanation);
        return _toolbar;
    }

    private void Run(TextAssistOperation operation)
    {
        var capture = _capture;
        if (capture is null) return;
        _toolbar?.ShowLoading();
        _ = ExecuteAsync(capture, operation);
    }

    private async Task ExecuteAsync(SelectionCapture capture, TextAssistOperation operation)
    {
        try
        {
            var anchor = capture.SelectedText.PointerPosition ?? pointerPosition.GetCurrent();
            await OnUiAsync(() => _toolbar?.Hide(), CancellationToken.None);
            if (operation == TextAssistOperation.Translation)
            {
                await translation.ShowSentenceAsync(capture.SelectedText.Text, anchor);
            }
            else
            {
                await textAssist.ShowResultAsync(capture.SelectedText.Text, operation, anchor);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to open the selected-text {Operation} result.", operation);
        }
        finally
        {
            await OnUiAsync(() => _toolbar?.HideLoading(), CancellationToken.None);
        }
    }

    private static bool Contains(Window? window, PhysicalScreenPoint point)
    {
        if (window?.IsVisible != true) return false;
        var client = window.PointToClient(new PixelPoint(point.X, point.Y));
        return new Rect(window.Bounds.Size).Contains(client);
    }

    private static void PositionToolbar(Window window, PhysicalScreenPoint point)
    {
        var screen = window.Screens.ScreenFromPoint(new PixelPoint(point.X, point.Y)) ?? window.Screens.Primary;
        if (screen is null)
        {
            window.Position = new PixelPoint(point.X + 6, point.Y + 6);
            return;
        }
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var offset = Math.Max(4, (int)Math.Ceiling(6 * scale));
        var width = Math.Max(1, (int)Math.Ceiling(window.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(window.Height * scale));
        var left = point.X + offset;
        var top = point.Y + offset;
        if (left + width > area.Right) left = point.X - width - offset;
        if (top + height > area.Bottom) top = point.Y - height - offset;
        left = Math.Clamp(left, area.X, Math.Max(area.X, area.Right - width));
        top = Math.Clamp(top, area.Y, Math.Max(area.Y, area.Bottom - height));
        window.Position = new PixelPoint(left, top);
    }

    private static async ValueTask OnUiAsync(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }
        await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken);
    }

    private static async ValueTask<T> OnUiAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Dispatcher.UIThread.CheckAccess()) return action();
        return await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken);
    }
}
