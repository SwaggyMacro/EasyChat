using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EasyChat.ViewModels.Pages;
using EasyChat.Models.Configuration;
using ReactiveUI;

using EasyChat.Models;

namespace EasyChat.Views.Speech;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public partial class SubtitleOverlayWindowView : Window
{
    public SubtitleOverlayWindowView()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SpeechRecognitionViewModel { IsFloatingWindowLocked: true }) return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
    

    // Smart Ghost Mode Logic
    private Avalonia.Threading.DispatcherTimer? _hitTestTimer;
    private bool _isTransparent;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is SpeechRecognitionViewModel { IsFloatingWindowLocked: true })
        {
             // Force initialization of Ghost Mode when window opens
             SetClickThrough(true);
             StartHitTestTimer();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SpeechRecognitionViewModel vm)
        {
            var observable = vm.WhenAnyValue(x => x.IsFloatingWindowLocked);
            observable.Subscribe(isLocked =>
            {
                // Unlocked: Transparent background (Window) to catch hover/resize events
                // Locked: Null background (Window) to allow click-through (visual only)
                
                this.Background = isLocked ? null : Avalonia.Media.Brushes.Transparent;
                
                var rootGrid = this.FindControl<Grid>("RootGrid");
                if (rootGrid != null) rootGrid.Background = null;

                if (isLocked)
                {
                    // Start Smart Hover Detection
                    // Init Button Opacity to 0 (Hidden until hover)
                    var btn = this.FindControl<Button>("UnlockBtn");
                    if (btn != null) btn.Opacity = 0;
                    
                    SetClickThrough(true);
                    StartHitTestTimer();
                }
                else
                {
                    // Stop Smart Hover Detection, restore interactive
                    StopHitTestTimer();
                    SetClickThrough(false);
                }
            });
            
            var orientObservable = vm.WhenAnyValue(x => x.FloatingWindowOrientation);
            orientObservable.Subscribe(orientation =>
            {
                UpdateOrientation(orientation);
            });
            
            // Auto Scroll Logic
            // Subscribe to existing items if any
            foreach (var item in vm.FloatingSubtitles)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
                item.PropertyChanged += OnItemPropertyChanged;
            }

            vm.FloatingSubtitles.CollectionChanged += (_, args) => 
            {
                if (args.NewItems != null)
                {
                    foreach (SubtitleItem item in args.NewItems)
                        item.PropertyChanged += OnItemPropertyChanged;
                }
                if (args.OldItems != null)
                {
                    foreach (SubtitleItem item in args.OldItems)
                        item.PropertyChanged -= OnItemPropertyChanged;
                }

                if (vm.FloatingDisplayMode == FloatingDisplayMode.AutoScroll) // Auto Scroll
                {
                    TriggerAutoScroll();
                }
            };
            
            // Trigger scroll when switching to AutoScroll
            vm.WhenAnyValue(x => x.FloatingDisplayMode).Subscribe((mode) => 
            {
                if (mode == FloatingDisplayMode.AutoScroll)
                {
                    TriggerAutoScroll();
                }
            });
        }
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (DataContext is SpeechRecognitionViewModel { FloatingDisplayMode: FloatingDisplayMode.AutoScroll } && 
            (e.PropertyName == nameof(SubtitleItem.OriginalText) || 
             e.PropertyName == nameof(SubtitleItem.DisplayTranslatedText) ||
             e.PropertyName == nameof(SubtitleItem.TranslatedText)))
        {
            TriggerAutoScroll();
        }
    }

    private async void TriggerAutoScroll()
    {
        // Delay slightly to ensure ItemControl has materialized the new container and updated layout
        await System.Threading.Tasks.Task.Delay(50);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => 
        {
            var scroll = this.FindControl<ScrollViewer>("SubtitlesScrollViewer");
            scroll?.ScrollToEnd();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void StartHitTestTimer()
    {
        if (_hitTestTimer == null)
        {
            _hitTestTimer = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _hitTestTimer.Tick += OnHitTestTick;
        }
        _hitTestTimer.Start();
    }

    private void StopHitTestTimer()
    {
        _hitTestTimer?.Stop();
    }

    private void OnHitTestTick(object? sender, EventArgs e)
    {
        // Logic: 
        // 1. Mouse in Window -> Show Button (Opacity=1)
        // 2. Mouse over Button -> Make Interactive
        
        var btn = this.FindControl<Button>("UnlockBtn");
        if (btn == null || !btn.IsVisible || !OperatingSystem.IsWindows()) return;

        GetCursorPos(out var pt);
        var mousePoint = new PixelPoint(pt.X, pt.Y);
        
        // Window Screen Bounds
        var winTopLeft = this.PointToScreen(new Point(0, 0));
        var winSize = PixelSize.FromSize(this.Bounds.Size, this.RenderScaling);
        var winRect = new PixelRect(winTopLeft, winSize);
        
        // Button Screen Bounds (Used for HitTest Interactivity)
        var btnTopLeft = btn.PointToScreen(new Point(0, 0));
        var btnSize = PixelSize.FromSize(btn.Bounds.Size, this.RenderScaling);
        var btnRect = new PixelRect(btnTopLeft, btnSize);
        
        bool isOverWindow = winRect.Contains(mousePoint);
        bool isOverButton = btnRect.Contains(mousePoint);
        
        // 1. Visibility Check
        if (isOverWindow)
        {
            if (btn.Opacity < 1) btn.Opacity = 1;
        }
        else
        {
            if (btn.Opacity > 0) btn.Opacity = 0;
        }
        
        // 2. Interactivity Check
        if (isOverButton && _isTransparent)
        {
            // Make Interactive
            SetClickThrough(false);
        }
        else if (!isOverButton && !_isTransparent)
        {
             // Make Ghost
             SetClickThrough(true);
        }
    }
    
    // Win32 Interop
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    private void SetClickThrough(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;
        
        var handle = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
        if (enabled)
        {
             // Enable Ghost
             if ((exStyle & WS_EX_TRANSPARENT) == 0)
                SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
        }
        else
        {
            // Disable Ghost (Interactive)
            if ((exStyle & WS_EX_TRANSPARENT) != 0)
                SetWindowLong(handle, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
        }
        _isTransparent = enabled;
    }

    private void OnResizePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginResizeDrag(WindowEdge.SouthEast, e);
        }
    }

    private void UpdateOrientation(string orientation)
    {
        // Only set defaults if we are in a "fresh" state or explicit reset is needed.
        // If the window is already sized (Manual), we should try to preserve it unless orientation shifts drastically?
        // Actually, preventing SizeToContent override is key.

        if (orientation == "Vertical")
        {
             MinWidth = 60;
             MaxWidth = 150;
             // If we want to enforce vertical strip look, we might constrain Width.
             if (Width > 150 || double.IsNaN(Width)) Width = 100;
             
             // Ensure we don't force SizeToContent if the user wants to resize Height
             if (SizeToContent != SizeToContent.Manual)
             {
                 SizeToContent = SizeToContent.Height;
             }
        }
        else
        {
            // Horizontal
            MinWidth = 200;
            MaxWidth = double.PositiveInfinity;
            
            if (Width < 200 || double.IsNaN(Width)) Width = 800;
            
            // Allow manual resizing by NOT enforcing SizeToContent if it's already set or if we want flexibility
            // But usually horizontal subtitles grow in height? 
            // If user says "Size Drag Save", they probably want a fixed box they can resize.
            // So let's switch to Manual if not already.
            
            SizeToContent = SizeToContent.Manual;
        }
    }
}
