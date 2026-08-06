using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using EasyChat.Contracts.Platform;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Foundation.Platform;
using EasyChat.Presentation.Shared.Feedback;
using Key = Avalonia.Input.Key;

namespace EasyChat.Presentation.Features.Capture.Views;

public partial class ResultView : Window
{
    private Screen? _screen;

    public ResultView() => InitializeComponent();

    public ResultView(
        SettingsSession settings,
        PhysicalScreenPoint completionPoint)
    {
        InitializeComponent();
        ApplyConfiguration(settings.Result);
        ShowLoading();
        IsVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        _screen = Screens.ScreenFromPoint(
            new PixelPoint(completionPoint.X, completionPoint.Y)) ?? Screens.Primary;
        if (_screen is not null)
        {
            TextBlockResult.MaxWidth = _screen.Bounds.Width / _screen.Scaling * 0.8;
            Position = new PixelPoint(_screen.Bounds.X, _screen.Bounds.Y);
        }
        Loaded += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                ReCenterPosition();
                if (IsLoaded)
                    IsVisible = true;
            });
        };
    }

    public void AppendText(string text) => Dispatcher.UIThread.Post(() =>
    {
        if (LoadingIndicator.IsVisible)
            ShowResult();
        TextBlockResult.Text += text;
        Dispatcher.UIThread.Post(ReCenterPosition);
    });

    public void ShowLoading() => Dispatcher.UIThread.Post(() =>
    {
        LoadingIndicator.IsVisible = true;
        TextBlockResult.IsVisible = false;
        if (ResultToolbar is not null)
            ResultToolbar.IsVisible = false;
    });

    public void ShowResult() => Dispatcher.UIThread.Post(() =>
    {
        LoadingIndicator.IsVisible = false;
        TextBlockResult.IsVisible = true;
        if (ResultToolbar is not null)
            ResultToolbar.IsVisible = true;
        ReCenterPosition();
    });

    private async void OnCopyClick(object? sender, RoutedEventArgs e) =>
        await CopyResultAsync(sender as Control);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        // Ctrl/Cmd+C copies result when the float is focused.
        if (e.Key == Key.C
            && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && ResultToolbar is { IsVisible: true })
        {
            e.Handled = true;
            await CopyResultAsync(CopyButton);
        }
    }

    private async Task CopyResultAsync(Control? anchor)
    {
        var text = TextBlockResult.Text;
        if (string.IsNullOrWhiteSpace(text))
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return;

        await clipboard.SetTextAsync(text);
        CopyFeedback.Show(anchor, EasyChat.Presentation.Lang.Resources.Copied);
        if (CopyHint is not null)
            CopyHint.IsVisible = true;
    }

    public void CloseAfterDelay(int milliseconds) => Dispatcher.UIThread.Post(async void () =>
    {
        await Task.Delay(milliseconds);
        Close();
    });

    private void ApplyConfiguration(LiveResultSettings settings)
    {
        TransparencyLevelHint = WindowTransparencyLevels.ForPreference(settings.TransparencyLevel);
        TrySetBrush(settings.BackgroundColor, brush => MainCard.Background = brush);
        TrySetBrush(settings.WindowBackgroundColor, brush => Background = brush);
        TrySetBrush(settings.FontColor, brush => TextBlockResult.Foreground = brush);
        TextBlockResult.FontSize = settings.FontSize;
        TextBlockResult.FontFamily = EcFontFamilies.Resolve(settings.FontFamily);
    }

    private static void TrySetBrush(string? value, Action<IBrush> apply)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        try
        {
            apply(Brush.Parse(value));
        }
        catch
        {
        }
    }

    private void ReCenterPosition()
    {
        if (_screen is null)
            return;

        var logicalWidth = Bounds.Width > 0 ? Bounds.Width : Width;
        Position = ScreenshotResultPlacement.CenterHorizontallyAtTop(
            _screen.Bounds,
            _screen.Scaling,
            logicalWidth,
            topOffsetDip: -5);
    }
}
