using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using EasyChat.Contracts.Input;
using EasyChat.Contracts.Platform;
using EasyChat.Presentation.Foundation.Platform;
using EasyChat.Presentation.Features.Input.Views;

namespace EasyChat.Presentation.Features.Input;

/// <summary>
/// Maps immutable TSF candidate events to a non-activating Avalonia surface. Translation policy
/// remains in Application and no native handle crosses that boundary.
/// </summary>
public sealed class TsfCandidateWindowCoordinator(
    ITsfInputTranslationUseCases useCases,
    IPlatformWindowBehavior windowBehavior) : IDisposable
{
    private readonly ITsfInputTranslationUseCases _useCases = useCases;
    private readonly IPlatformWindowBehavior _windowBehavior = windowBehavior;
    private TsfCandidateWindowView? _window;
    private int _started;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;
        _useCases.CandidateChanged += OnCandidateChanged;
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
            return;
        _useCases.CandidateChanged -= OnCandidateChanged;
        Dispatcher.UIThread.Post(Hide);
    }

    public void Dispose() => Stop();

    private void OnCandidateChanged(object? sender, TsfCandidateChanged candidate) =>
        Dispatcher.UIThread.Post(() => Apply(candidate), DispatcherPriority.Input);

    private void Apply(TsfCandidateChanged candidate)
    {
        if (candidate.Status is TsfCandidateStatus.Hidden || candidate.CaretRegion is null)
        {
            Hide();
            return;
        }

        _window ??= CreateWindow();
        _window.Update(candidate);
        if (!_window.IsVisible)
            _window.Show();
        _ = ConfigureAndPlaceAsync(_window, candidate.CaretRegion.Value);
    }

    private TsfCandidateWindowView CreateWindow()
    {
        var window = new TsfCandidateWindowView
        {
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowDecorations = Avalonia.Controls.WindowDecorations.None,
            Background = Avalonia.Media.Brushes.Transparent
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
                _window = null;
        };
        return window;
    }

    private async Task ConfigureAndPlaceAsync(TsfCandidateWindowView window, PhysicalScreenRegion caret)
    {
        try
        {
            await _windowBehavior.ConfigureNoActivateAsync(window).ConfigureAwait(true);
            await Dispatcher.UIThread.InvokeAsync(() => Place(window, caret));
        }
        catch (InvalidOperationException)
        {
            // The native handle may not exist during the first layout pass; the next update retries.
        }
    }

    private static void Place(TsfCandidateWindowView window, PhysicalScreenRegion caret)
    {
        var screen = window.Screens.ScreenFromPoint(new PixelPoint(caret.X, caret.Y))
            ?? window.Screens.Primary;
        if (screen is null)
            return;

        var work = screen.WorkingArea;
        var width = Math.Max(220, (int)Math.Ceiling(window.Bounds.Width));
        var height = Math.Max(56, (int)Math.Ceiling(window.Bounds.Height));
        var x = Math.Clamp(caret.X, work.X, work.Right - width);
        var below = caret.Y + caret.Height + 6;
        var y = below + height <= work.Bottom
            ? below
            : Math.Max(work.Y, caret.Y - height - 6);
        window.Position = new PixelPoint(x, y);
    }

    private void Hide() => _window?.Hide();
}
