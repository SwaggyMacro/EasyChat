using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EasyChat.Services.Abstractions;
using EasyChat.Views;
using EasyChat.Views.Overlay;

using EasyChat.Constants;
using EasyChat.Models;

namespace EasyChat.Services.ScreenCapture;

public class ScreenSelectionSession
{
    private readonly Action<Bitmap, CaptureIntent> _onCapture;
    private readonly Action? _onCancel;
    private readonly List<OverlayWindow> _overlays = new();
    private readonly IScreenCaptureService _screenCaptureService;
    private readonly string _mode;

    public ScreenSelectionSession(IScreenCaptureService screenCaptureService, Action<Bitmap, CaptureIntent> onCapture, Action? onCancel = null, string mode = Constant.ScreenshotMode.Precise)
    {
        _screenCaptureService = screenCaptureService;
        _onCapture = onCapture;
        _onCancel = onCancel;
        _mode = mode;
    }

    public void Start()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        var screens = desktop.Windows.FirstOrDefault(w => w is MainWindow)?.Screens.All ?? Array.Empty<Screen>();

        if (screens.Count == 0) return;

        var minX = screens.Min(s => s.Bounds.X);
        var minY = screens.Min(s => s.Bounds.Y);
        var maxX = screens.Max(s => s.Bounds.X + s.Bounds.Width);
        var maxY = screens.Max(s => s.Bounds.Y + s.Bounds.Height);

        var width = maxX - minX;
        var height = maxY - minY;

        // Capture full virtual screen content
        var bitmap = _screenCaptureService.CaptureRegion(minX, minY, width, height);

        var bounds = new PixelRect(minX, minY, width, height);

        // Create overlay
        var overlay = new OverlayWindow(bounds, bitmap, _mode);
        overlay.SelectionCompleted += OnSelectionCompleted;
        overlay.SelectionCanceled += OnSelectionCanceled;

        _overlays.Add(overlay);
        overlay.Show();
    }

    private void OnSelectionCompleted(Bitmap bitmap, CaptureIntent intent)
    {
        CloseAll();
        _onCapture(bitmap, intent);
    }

    private void OnSelectionCanceled()
    {
        CloseAll();
        _onCancel?.Invoke();
    }

    private void CloseAll()
    {
        foreach (var overlay in _overlays)
        {
            overlay.SelectionCompleted -= OnSelectionCompleted;
            overlay.SelectionCanceled -= OnSelectionCanceled;
            overlay.Close();
        }

        _overlays.Clear();
    }
}