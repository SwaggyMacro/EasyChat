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

namespace EasyChat.Services.ScreenCapture;

public class ScreenSelectionSession
{
    private readonly Action<Bitmap> _onCapture;
    private readonly Action? _onCancel;
    private readonly List<OverlayWindow> _overlays = new();
    private readonly IScreenCaptureService _screenCaptureService;

    public ScreenSelectionSession(IScreenCaptureService screenCaptureService, Action<Bitmap> onCapture, Action? onCancel = null)
    {
        _screenCaptureService = screenCaptureService;
        _onCapture = onCapture;
        _onCancel = onCancel;
    }

    public void Start()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        var screens = desktop.Windows.FirstOrDefault(w => w is MainWindow)?.Screens.All ?? Array.Empty<Screen>();

        foreach (var screen in screens)
        {
            // Capture full screen content
            var bitmap = _screenCaptureService.CaptureFullScreen(screen);

            // Create overlay
            var overlay = new OverlayWindow(screen, bitmap);
            overlay.SelectionCompleted += OnSelectionCompleted;
            overlay.SelectionCanceled += OnSelectionCanceled;

            _overlays.Add(overlay);
            overlay.Show();
        }
    }

    private void OnSelectionCompleted(Bitmap bitmap)
    {
        CloseAll();
        _onCapture(bitmap);
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