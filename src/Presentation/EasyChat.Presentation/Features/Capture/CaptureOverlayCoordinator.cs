using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EasyChat.Contracts.Capture;
using EasyChat.Contracts.Platform;
using EasyChat.Presentation.Features.Capture.Views;
using EasyChat.Presentation.ImageTranslation;

namespace EasyChat.Presentation.Features.Capture;

internal sealed record CaptureOverlayOutcome(
    PhysicalScreenRegion Region,
    PhysicalScreenPoint CompletionPoint,
    CaptureOverlayAction Action,
    Bitmap? Image);

public sealed class CaptureOverlayCoordinator(
    IScreenCatalog screens,
    IScreenCapture capture,
    IPointerPosition pointer)
{
    private readonly IScreenCatalog _screens = screens;
    private readonly IScreenCapture _capture = capture;
    private readonly IPointerPosition _pointer = pointer;
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal async Task<CaptureOverlayOutcome?> SelectAsync(
        bool precise,
        bool regionOnly,
        CaptureOverlayAction defaultAction = CaptureOverlayAction.Translation,
        CaptureToolbarMode toolbarMode = CaptureToolbarMode.Full,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var availableScreens = (await _screens.GetScreensAsync(cancellationToken)
                    .ConfigureAwait(false))
                .Where(screen => !screen.Bounds.IsEmpty)
                .ToArray();
            if (availableScreens.Length == 0)
                throw new InvalidOperationException("No display screen is available.");

            var desktopBounds = Union(availableScreens.Select(screen => screen.Bounds));
            using var desktopImage = await CaptureDesktopImageAsync(
                desktopBounds,
                cancellationToken).ConfigureAwait(false);
            var initialPointer = GetInitialPointer(availableScreens);
            var session = await OnUiAsync(
                () => new CaptureOverlaySession(
                    availableScreens,
                    desktopBounds,
                    desktopImage,
                    precise,
                    regionOnly,
                    defaultAction,
                    regionOnly ? CaptureToolbarMode.ImageSelection : toolbarMode),
                cancellationToken);
            try
            {
                var completion = await OnUiAsync(
                    () => session.Start(initialPointer),
                    cancellationToken);
                using var cancellationRegistration = cancellationToken.Register(() =>
                    Dispatcher.UIThread.Post(() => session.Cancel(cancellationToken)));
                return await completion.ConfigureAwait(false);
            }
            finally
            {
                await OnUiAsync(session.Dispose, CancellationToken.None);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Bitmap> CaptureDesktopImageAsync(
        PhysicalScreenRegion desktopBounds,
        CancellationToken cancellationToken)
    {
        var captured = await _capture.CaptureAsync(
            new ScreenCaptureRequest(ScreenCaptureTarget.Region, Region: desktopBounds),
            cancellationToken).ConfigureAwait(false);
        if (captured.IsFailure)
            throw new InvalidOperationException(captured.Error.Message);
        return AvaloniaImageFrames.ToBitmap(captured.Value);
    }

    private PhysicalScreenPoint GetInitialPointer(IReadOnlyList<ScreenDescriptor> availableScreens)
    {
        try
        {
            return _pointer.GetCurrent();
        }
        catch
        {
            var primary = availableScreens.FirstOrDefault(screen => screen.IsPrimary) ?? availableScreens[0];
            return new PhysicalScreenPoint(
                primary.Bounds.X + primary.Bounds.Width / 2,
                primary.Bounds.Y + primary.Bounds.Height / 2);
        }
    }

    private static PhysicalScreenRegion Union(IEnumerable<PhysicalScreenRegion> regions)
    {
        var all = regions.Where(region => !region.IsEmpty).ToArray();
        if (all.Length == 0)
            throw new InvalidOperationException("No non-empty display screen is available.");
        var left = all.Min(region => region.X);
        var top = all.Min(region => region.Y);
        var right = all.Max(region => checked(region.X + region.Width));
        var bottom = all.Max(region => checked(region.Y + region.Height));
        return new PhysicalScreenRegion(left, top, right - left, bottom - top);
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
        return await Dispatcher.UIThread.InvokeAsync(
            action,
            DispatcherPriority.Normal,
            cancellationToken);
    }
}

internal sealed class CaptureOverlaySession : IDisposable
{
    private readonly IReadOnlyList<ScreenDescriptor> _screens;
    private readonly PhysicalScreenRegion _desktopBounds;
    private readonly Bitmap _desktopImage;
    private readonly bool _precise;
    private readonly bool _regionOnly;
    private readonly CaptureOverlayAction _defaultAction;
    private readonly PhysicalSelectionState _selection;
    private readonly List<OverlaySurface> _surfaces = [];
    private readonly TaskCompletionSource<CaptureOverlayOutcome?> _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private OverlayWindowView? _toolbarView;
    private OverlayWindowView? _hintView;
    private PhysicalScreenPoint? _completionPoint;
    private Screens? _screenCollection;
    private bool _finished;
    private bool _disposed;

    public CaptureOverlaySession(
        IReadOnlyList<ScreenDescriptor> screens,
        PhysicalScreenRegion desktopBounds,
        Bitmap desktopImage,
        bool precise,
        bool regionOnly,
        CaptureOverlayAction defaultAction,
        CaptureToolbarMode toolbarMode)
    {
        _screens = screens;
        _desktopBounds = desktopBounds;
        _desktopImage = desktopImage;
        _precise = precise;
        _regionOnly = regionOnly;
        _defaultAction = defaultAction;
        _selection = new PhysicalSelectionState(ToPixelRect(desktopBounds));

        try
        {
            foreach (var screen in screens)
            {
                var crop = CaptureOverlayGeometry.GetDesktopSlice(
                    screen.Bounds,
                    desktopBounds);
                var background = new CroppedBitmap(desktopImage, crop);
                OverlayWindowView? view = null;
                try
                {
                    view = new OverlayWindowView(
                        screen,
                        background,
                        regionOnly,
                        defaultAction,
                        toolbarMode);
                    Subscribe(view);
                    _surfaces.Add(new OverlaySurface(view, background));
                }
                catch
                {
                    if (view is not null)
                    {
                        Unsubscribe(view);
                        view.PrepareForSessionClose();
                    }
                    background.Dispose();
                    throw;
                }
            }
        }
        catch
        {
            foreach (var surface in _surfaces)
            {
                Unsubscribe(surface.View);
                surface.View.PrepareForSessionClose();
                surface.Background.Dispose();
            }
            throw;
        }
    }

    public Task<CaptureOverlayOutcome?> Start(PhysicalScreenPoint initialPointer)
    {
        ThrowIfDisposed();
        var active = FindView(initialPointer)
                     ?? _surfaces.FirstOrDefault(surface => surface.View.Screen.IsPrimary)?.View
                     ?? _surfaces[0].View;
        _hintView = active;

        foreach (var surface in _surfaces)
        {
            surface.View.SetHintHost(ReferenceEquals(surface.View, active));
            surface.View.Show();
        }

        _screenCollection = active.Screens;
        _screenCollection.Changed += OnScreensChanged;
        if (!CaptureOverlayGeometry.MatchesTopology(
                _screens,
                _screenCollection.All.Select(screen => (screen.Bounds, screen.Scaling))))
        {
            Finish(null);
            return _completion.Task;
        }
        RenderAll();
        active.Activate();
        return _completion.Task;
    }

    public void Cancel(CancellationToken cancellationToken)
    {
        if (_finished)
            return;
        _finished = true;
        var closeFailure = CloseAll();
        if (closeFailure is null)
            _completion.TrySetCanceled(cancellationToken);
        else
            _completion.TrySetException(closeFailure);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_screenCollection is not null)
            _screenCollection.Changed -= OnScreensChanged;
        _ = CloseAll();
        foreach (var surface in _surfaces)
        {
            try
            {
                Unsubscribe(surface.View);
                surface.View.PrepareForSessionClose();
            }
            catch
            {
            }
            try
            {
                surface.Background.Dispose();
            }
            catch
            {
            }
        }
        _surfaces.Clear();
    }

    private void Subscribe(OverlayWindowView view)
    {
        view.InteractionStarted += OnInteractionStarted;
        view.InteractionMoved += OnInteractionMoved;
        view.InteractionEnded += OnInteractionEnded;
        view.ActionRequested += OnActionRequested;
        view.ResetRequested += OnResetRequested;
        view.CancelRequested += OnCancelRequested;
        view.ClosedUnexpectedly += OnClosedUnexpectedly;
    }

    private void Unsubscribe(OverlayWindowView view)
    {
        view.InteractionStarted -= OnInteractionStarted;
        view.InteractionMoved -= OnInteractionMoved;
        view.InteractionEnded -= OnInteractionEnded;
        view.ActionRequested -= OnActionRequested;
        view.ResetRequested -= OnResetRequested;
        view.CancelRequested -= OnCancelRequested;
        view.ClosedUnexpectedly -= OnClosedUnexpectedly;
    }

    private void OnInteractionStarted(
        OverlayWindowView view,
        PhysicalScreenPoint point,
        CaptureResizeHandle handle,
        bool insideSelection)
    {
        if (_finished)
            return;

        _hintView = view;
        var pixel = ToPixelPoint(point);
        if (_selection.Mode == CaptureSelectionMode.Done &&
            handle != CaptureResizeHandle.None &&
            _selection.BeginResize(handle, pixel))
        {
            _toolbarView = null;
            _completionPoint = null;
            RenderAll();
            return;
        }
        if (_selection.Mode == CaptureSelectionMode.Done &&
            insideSelection &&
            _selection.BeginMove(pixel))
        {
            _toolbarView = null;
            _completionPoint = null;
            RenderAll();
            return;
        }

        _selection.BeginSelection(pixel);
        _toolbarView = null;
        _completionPoint = null;
        foreach (var surface in _surfaces)
            surface.View.SetHintHost(false);
        RenderAll();
    }

    private void OnInteractionMoved(OverlayWindowView view, PhysicalScreenPoint point)
    {
        if (_finished || _selection.Mode is CaptureSelectionMode.Idle or CaptureSelectionMode.Done)
            return;
        _selection.Update(ToPixelPoint(point));
        RenderAll();
    }

    private void OnInteractionEnded(OverlayWindowView view, PhysicalScreenPoint point)
    {
        if (_finished || _selection.Mode is CaptureSelectionMode.Idle or CaptureSelectionMode.Done)
            return;
        _selection.Update(ToPixelPoint(point));
        if (!_selection.Complete(ToPixelPoint(point)))
        {
            Reset();
            return;
        }

        _toolbarView = FindToolbarView(point, view);
        _hintView = _toolbarView;
        _completionPoint = point;
        if (_precise)
        {
            RenderAll();
            _toolbarView.Activate();
        }
        else
            Complete(_defaultAction);
    }

    private void OnActionRequested(OverlayWindowView view, CaptureOverlayAction action)
    {
        if (_finished || _selection.Mode != CaptureSelectionMode.Done)
            return;
        _toolbarView = view;
        _completionPoint = ScreenCenter(view.Screen.Bounds);
        Complete(action);
    }

    private void OnResetRequested() => Reset();
    private void OnCancelRequested() => Finish(null);
    private void OnClosedUnexpectedly() => Finish(null);
    private void OnScreensChanged(object? sender, EventArgs e) => Finish(null);

    private void Reset()
    {
        if (_finished)
            return;
        _selection.Reset();
        _toolbarView = null;
        _completionPoint = null;
        _hintView ??= _surfaces.FirstOrDefault(surface => surface.View.Screen.IsPrimary)?.View
                      ?? _surfaces[0].View;
        foreach (var surface in _surfaces)
            surface.View.SetHintHost(ReferenceEquals(surface.View, _hintView));
        RenderAll();
    }

    private void Complete(CaptureOverlayAction action)
    {
        if (_selection.Region is not { } selected ||
            _completionPoint is not { } completionPoint)
        {
            Finish(null);
            return;
        }

        Bitmap? image = null;
        try
        {
            if (!_regionOnly)
            {
                var crop = _selection.ToUnionBitmapRect(ToPixelRect(_desktopBounds));
                if (crop is not { Width: > 0, Height: > 0 } cropRect)
                {
                    Finish(null);
                    return;
                }

                image = RenderCrop(_desktopImage, cropRect);
            }

            var outcome = new CaptureOverlayOutcome(
                ToPhysicalRegion(selected),
                completionPoint,
                action,
                image);
            Finish(outcome);
            image = null;
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
        finally
        {
            image?.Dispose();
        }
    }

    private void Finish(CaptureOverlayOutcome? outcome)
    {
        if (_finished)
        {
            outcome?.Image?.Dispose();
            return;
        }
        _finished = true;
        var closeFailure = CloseAll();
        if (closeFailure is null)
        {
            _completion.TrySetResult(outcome);
        }
        else
        {
            outcome?.Image?.Dispose();
            _completion.TrySetException(closeFailure);
        }
    }

    private void Fail(Exception exception)
    {
        if (_finished)
            return;
        _finished = true;
        var closeFailure = CloseAll();
        _completion.TrySetException(closeFailure is null
            ? exception
            : new AggregateException(exception, closeFailure));
    }

    private void RenderAll()
    {
        PhysicalScreenRegion? region = _selection.Region is { } value
            ? ToPhysicalRegion(value)
            : null;
        foreach (var surface in _surfaces)
        {
            surface.View.RenderSelection(
                region,
                _selection.Mode,
                showToolbar: ReferenceEquals(surface.View, _toolbarView));
        }
    }

    private OverlayWindowView? FindView(PhysicalScreenPoint point) =>
        _surfaces.Select(surface => surface.View).FirstOrDefault(view =>
            Contains(view.Screen.Bounds, point));

    private OverlayWindowView FindToolbarView(
        PhysicalScreenPoint point,
        OverlayWindowView fallback)
    {
        if (_selection.Region is not { } selection)
            return fallback;
        var candidates = _surfaces
            .Select(surface => surface.View)
            .Where(view => PhysicalPixelGeometry.Intersect(
                selection,
                ToPixelRect(view.Screen.Bounds)) is not null)
            .ToArray();
        return candidates.FirstOrDefault(view => Contains(view.Screen.Bounds, point))
               ?? candidates.FirstOrDefault(view => ContainsInclusive(view.Screen.Bounds, point))
               ?? candidates.FirstOrDefault(view => ReferenceEquals(view, fallback))
               ?? candidates.FirstOrDefault()
               ?? fallback;
    }

    private Exception? CloseAll()
    {
        List<Exception>? failures = null;
        foreach (var surface in _surfaces)
        {
            try
            {
                surface.View.CloseSessionWindow();
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }
        return failures?.Count switch
        {
            null or 0 => null,
            1 => failures[0],
            _ => new AggregateException(failures)
        };
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static PixelRect ToPixelRect(PhysicalScreenRegion region) =>
        new(region.X, region.Y, region.Width, region.Height);

    private static PixelPoint ToPixelPoint(PhysicalScreenPoint point) => new(point.X, point.Y);

    private static PhysicalScreenPoint ToPhysicalPoint(PixelPoint point) => new(point.X, point.Y);

    private static PhysicalScreenRegion ToPhysicalRegion(PixelRect region) =>
        new(region.X, region.Y, region.Width, region.Height);

    private static RenderTargetBitmap RenderCrop(Bitmap sourceBitmap, PixelRect crop)
    {
        using var source = new CroppedBitmap(sourceBitmap, crop);
        var result = new RenderTargetBitmap(crop.Size, new Vector(96, 96));
        try
        {
            using (var context = result.CreateDrawingContext())
                context.DrawImage(source, new Rect(0, 0, crop.Width, crop.Height));
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static bool Contains(PhysicalScreenRegion region, PhysicalScreenPoint point) =>
        point.X >= region.X && point.X < checked(region.X + region.Width) &&
        point.Y >= region.Y && point.Y < checked(region.Y + region.Height);

    private static bool ContainsInclusive(PhysicalScreenRegion region, PhysicalScreenPoint point) =>
        point.X >= region.X && point.X <= checked(region.X + region.Width) &&
        point.Y >= region.Y && point.Y <= checked(region.Y + region.Height);

    private static PhysicalScreenPoint ScreenCenter(PhysicalScreenRegion region) => new(
        region.X + region.Width / 2,
        region.Y + region.Height / 2);

    private sealed record OverlaySurface(OverlayWindowView View, CroppedBitmap Background);
}
