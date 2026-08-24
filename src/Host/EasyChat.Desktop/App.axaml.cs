using System.Reactive.Concurrency;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using EasyChat.Contracts.Settings;
using EasyChat.Presentation.Features.Capture;
using EasyChat.Presentation.Features.Shell;
using EasyChat.Presentation.Features.Shell.Views;
using Material.Icons;
using Material.Icons.Avalonia;
using ReactiveUI;
using ShadUI;

namespace EasyChat.Desktop;

public sealed partial class App(
    Func<DesktopUiContext>? createUiContext,
    Action<Action>? registerActivationHandler = null,
    bool startInTray = false) : Avalonia.Application
{
    private const int TrayMenuIconSize = 36;
    private const int TrayMenuGlyphSize = 34;

    public App()
        : this(null, null)
    {
    }

    private DesktopUiContext? _ui;
    private TrayIcon? _trayIcon;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _mainWindow;
    private bool _isHiddenToTray;
    private readonly bool _startInTray = startInTray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (SynchronizationContext.Current is { } synchronizationContext)
            RxApp.MainThreadScheduler = new SynchronizationContextScheduler(synchronizationContext);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (createUiContext is null)
            {
                desktop.MainWindow = new MainWindow();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            var ui = createUiContext();
            _ui = ui;
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            _mainWindow = new MainWindow(
                ui.MainWindowViewModel,
                ui.Settings,
                ui.MainWindowViewModel.ShadDialogManager,
                PrepareForTray);
            if (_startInTray)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                PrepareForTray();
            }
            else
            {
                desktop.MainWindow = _mainWindow;
            }
            registerActivationHandler?.Invoke(() =>
                Avalonia.Threading.Dispatcher.UIThread.Post(
                    ShowMainWindow,
                    Avalonia.Threading.DispatcherPriority.Input));
            desktop.Exit += OnExit;
            ActualThemeVariantChanged += OnThemeVariantChanged;
            ui.Settings.Changed += OnSettingsChanged;
            UpdateTrayIcon(ui.Settings.General.ClosingBehavior);
            ui.Interactions.Start();
            ui.TsfCandidates.Start();
            _ = WarmUpScreenshotCaptureAsync(ui.ScreenshotCapture);
#if !DEBUG
            _ = CheckForUpdatesAsync();
#endif
        }
        base.OnFrameworkInitializationCompleted();
        if (_ui is { } initializedUi)
        {
            // Start the screenshot worker after Avalonia has completed its
            // lifetime/window initialization, while keeping startup non-blocking.
            Dispatcher.UIThread.Post(
                () => _ = WarmUpScreenshotCaptureAsync(initializedUi.ScreenshotCapture),
                DispatcherPriority.Background);
        }
#if DEBUG
        if (_desktop is not null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(
                ShowUpdateToastPreview,
                Avalonia.Threading.DispatcherPriority.Background);
        }
#endif
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (args.Section == SettingsSection.General)
            UpdateTrayIcon(args.Current.General.ClosingBehavior);
    }

    private void UpdateTrayIcon(ClosingBehavior behavior)
    {
        if (behavior == ClosingBehavior.MinimizeToTray || _isHiddenToTray)
            EnsureTrayIcon();
        else
            RemoveTrayIcon();
    }

    private void PrepareForTray()
    {
        _isHiddenToTray = true;
        EnsureTrayIcon();
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon is not null) return;
        using var stream = AssetLoader.Open(new Uri("avares://EasyChat.Desktop/Assets/easychat-logo.ico"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(stream),
            ToolTipText = EasyChat.Presentation.Lang.Resources.AppName,
            Menu = CreateTrayMenu(),
            IsVisible = true
        };
        _trayIcon.Clicked += OnTrayShow;
        var icons = GetValue(TrayIcon.IconsProperty) ?? new TrayIcons();
        if (GetValue(TrayIcon.IconsProperty) is null)
            SetValue(TrayIcon.IconsProperty, icons);
        icons.Add(_trayIcon);
    }

    private NativeMenu CreateTrayMenu()
    {
        var menu = new NativeMenu();
        var show = new NativeMenuItem(EasyChat.Presentation.Lang.Resources.TrayShow)
        {
            Icon = CreateTrayMenuIcon(MaterialIconKind.WindowRestore)
        };
        show.Click += OnTrayShow;
        menu.Items.Add(show);
        menu.Items.Add(new NativeMenuItemSeparator());
        var exit = new NativeMenuItem(EasyChat.Presentation.Lang.Resources.TrayExit)
        {
            Icon = CreateTrayMenuIcon(MaterialIconKind.Power)
        };
        exit.Click += OnTrayExit;
        menu.Items.Add(exit);
        return menu;
    }

    private Bitmap CreateTrayMenuIcon(MaterialIconKind kind)
    {
        var geometry = StreamGeometry.Parse(MaterialIconDataProvider.GetData(kind));
        var glyph = new Avalonia.Controls.Shapes.Path
        {
            Data = geometry,
            Fill = ResolveTrayMenuIconBrush(),
            Width = TrayMenuGlyphSize,
            Height = TrayMenuGlyphSize,
            Stretch = Stretch.Uniform
        };
        var canvas = new Canvas
        {
            Width = TrayMenuIconSize,
            Height = TrayMenuIconSize,
            UseLayoutRounding = true
        };
        Canvas.SetLeft(glyph, (TrayMenuIconSize - TrayMenuGlyphSize) / 2d);
        Canvas.SetTop(glyph, (TrayMenuIconSize - TrayMenuGlyphSize) / 2d);
        canvas.Children.Add(glyph);
        canvas.Measure(new Size(TrayMenuIconSize, TrayMenuIconSize));
        canvas.Arrange(new Rect(0, 0, TrayMenuIconSize, TrayMenuIconSize));

        var bitmap = new RenderTargetBitmap(
            new PixelSize(TrayMenuIconSize, TrayMenuIconSize),
            new Vector(96, 96));
        bitmap.Render(canvas);
        return bitmap;
    }

    private IBrush ResolveTrayMenuIconBrush()
    {
        if (TryGetResource("SystemControlForegroundBaseHighBrush", ActualThemeVariant, out var value)
            && value is IBrush brush)
        {
            return brush;
        }

        return ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
            ? Brushes.White
            : Brushes.Black;
    }

    private void OnThemeVariantChanged(object? sender, EventArgs args)
    {
        if (_trayIcon?.Menu is not { Items.Count: >= 3 } menu
            || menu.Items[0] is not NativeMenuItem show
            || menu.Items[2] is not NativeMenuItem exit)
        {
            return;
        }

        ReplaceTrayMenuIcon(show, MaterialIconKind.WindowRestore);
        ReplaceTrayMenuIcon(exit, MaterialIconKind.Power);
    }

    private void ReplaceTrayMenuIcon(NativeMenuItem item, MaterialIconKind kind)
    {
        var previous = item.Icon as IDisposable;
        item.Icon = CreateTrayMenuIcon(kind);
        previous?.Dispose();
    }

    private void RemoveTrayIcon()
    {
        if (_trayIcon is null) return;
        _trayIcon.Clicked -= OnTrayShow;
        DisposeTrayMenuIcons(_trayIcon.Menu);
        if (GetValue(TrayIcon.IconsProperty) is { } icons)
            icons.Remove(_trayIcon);
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private static void DisposeTrayMenuIcons(NativeMenu? menu)
    {
        if (menu is null) return;
        foreach (var menuItem in menu.Items)
        {
            if (menuItem is not NativeMenuItem item || item.Icon is not IDisposable icon) continue;
            item.Icon = null;
            icon.Dispose();
        }
    }

    private void OnTrayShow(object? sender, EventArgs args)
    {
        ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        if (_desktop is { MainWindow: null } desktop)
        {
            desktop.MainWindow = _mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        _isHiddenToTray = false;
        if (_ui is { } ui)
            UpdateTrayIcon(ui.Settings.General.ClosingBehavior);
    }

    private void OnTrayExit(object? sender, EventArgs args)
    {
        if (_mainWindow is not null) _mainWindow.IsExiting = true;
        _desktop?.Shutdown();
    }

    private async Task CheckForUpdatesAsync()
    {
        var ui = RequireUi();
        var result = await ui.Updates.CheckAsync();
        if (result.IsFailure || !result.Value.IsUpdateAvailable) return;
        ShowUpdateToast(ui, result.Value.LatestVersion, () => _ = DownloadUpdateAsync(ui));
    }

    private static void ShowUpdateToast(
        DesktopUiContext ui,
        string latestVersion,
        Action updateAction)
    {
        var content = UpdateToastContentFactory.CreateAvailabilityContent(
            latestVersion,
            ui.UpdateToasts.DismissAll,
            updateAction);

        ui.UpdateToasts
            .CreateToast(EasyChat.Presentation.Lang.Resources.NewVersionAvailable)
            .WithContent(content)
            .WithDelay(0)
            .Show();
    }

#if DEBUG
    private void ShowUpdateToastPreview()
    {
        if (_ui is not { } ui) return;
        ShowUpdateToast(
            ui,
            $"{ui.Updates.CurrentVersion} (debug preview)",
            () => _ = ShowDebugUpdateProgressAsync(ui));
    }

    private static async Task ShowDebugUpdateProgressAsync(DesktopUiContext ui)
    {
        var content = UpdateToastContentFactory.CreateProgressContent(out var progress, out var progressText);
        ui.UpdateToasts.DismissAll();
        ui.UpdateToasts
            .CreateToast(EasyChat.Presentation.Lang.Resources.Updating)
            .WithContent(content)
            .WithDelay(0)
            .Show();

        for (var value = 0; value <= 100; value += 5)
        {
            progress.Value = value;
            progressText.Text = $"{value}%";
            await Task.Delay(70);
        }
    }
#endif

    private static async Task WarmUpScreenshotCaptureAsync(IScreenshotCaptureSession capture)
    {
        try
        {
            await capture.WarmUpAsync();
        }
        catch
        {
            // Capture retries by rebuilding the worker when the shortcut is used.
        }
    }

    private static async Task DownloadUpdateAsync(DesktopUiContext ui)
    {
        var content = UpdateToastContentFactory.CreateProgressContent(out var progress, out var progressText);
        ui.UpdateToasts.DismissAll();
        ui.UpdateToasts
            .CreateToast(EasyChat.Presentation.Lang.Resources.Updating)
            .WithContent(content)
            .WithDelay(0)
            .Show();
        var result = await ui.Updates.DownloadAndRestartAsync(UpdateToastContentFactory.CreateProgressReporter(value =>
        {
            progress.Value = Math.Clamp(value, 0, 100);
            progressText.Text = $"{progress.Value:0}%";
        }));
        ui.UpdateToasts.DismissAll();
        if (result.IsFailure)
            ui.UpdateToasts
                .CreateToast(EasyChat.Presentation.Lang.Resources.UpdateFailed)
                .WithContent(EasyChat.Presentation.Lang.Resources.CheckNetwork)
                .WithDelay(5)
                .ShowError();
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs args)
    {
        ActualThemeVariantChanged -= OnThemeVariantChanged;
        RemoveTrayIcon();
        if (_ui is { } ui)
        {
            ui.Settings.Changed -= OnSettingsChanged;
            ui.Interactions.Stop();
            ui.TsfCandidates.Stop();
            _ui = null;
        }
    }

    private DesktopUiContext RequireUi() =>
        _ui ?? throw new InvalidOperationException("Desktop UI has not been initialized.");
}
