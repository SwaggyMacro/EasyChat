using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using EasyChat.Services.Abstractions;
using EasyChat.Views.Windows;
using Microsoft.Extensions.Logging;
using EasyChat.Common;
using ReactiveUI;

namespace EasyChat.Services.Translation;

public class SelectionTranslationService : IDisposable
{
    private readonly IMouseHookService _mouseHookService;
    private readonly IConfigurationService _configurationService;
    private readonly IPlatformService _platformService;
    private readonly ILogger<SelectionTranslationService> _logger;

    private (int x, int y)? _downPoint;
    
    // Thresholds
    private const int DragThreshold = 5; // pixels
    
    private SelectionIconWindow? _iconWindow;
    private int _lastIconX;
    private int _lastIconY;
    private string? _lastSelectedText;

    public SelectionTranslationService(
        IMouseHookService mouseHookService,
        IConfigurationService configurationService,
        IPlatformService platformService,
        ILogger<SelectionTranslationService> logger)
    {
        _mouseHookService = mouseHookService;
        _configurationService = configurationService;
        _platformService = platformService;
        _logger = logger;

        _mouseHookService.MouseDown += OnMouseDown;
        _mouseHookService.MouseUp += OnMouseUp;
        
        // Reactive config monitoring
        if (_configurationService.SelectionTranslation != null)
        {
            // Flag to track if this is the initial subscription callback (app startup)
            bool isStartup = true;

            _configurationService.SelectionTranslation.WhenAnyValue(x => x.Enabled)
                .Subscribe(enabled =>
                {
                    if (enabled)
                    {
                        if (isStartup)
                        {
                            // Delay start strictly for startup to prevent lag
                            Task.Delay(3000).ContinueWith(_ =>
                            {
                                Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    // Re-check enabled state after delay in case it changed
                                    if (_configurationService.SelectionTranslation.Enabled)
                                    {
                                        StartHook();
                                    }
                                });
                            });
                        }
                        else
                        {
                            // Immediate start for runtime toggle
                            Dispatcher.UIThread.InvokeAsync(StartHook);
                        }
                    }
                    else
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                             _mouseHookService.Stop();
                        });
                    }
                    
                    // After the first callback (immediate initial value), subsequent ones are runtime changes
                    isStartup = false;
                });
        }
        
        _logger.LogInformation("SelectionTranslationService initialized");
    }

    private void StartHook()
    {
        try
        {
            _mouseHookService.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start mouse hook service");
        }
    }

    private void OnMouseDown(object? sender, SimpleMouseEventArgs e)
    {
        if (_configurationService.SelectionTranslation?.Enabled != true) 
        {
            return;
        }
        
        _logger.LogDebug("Mouse Down at {X}, {Y}", e.X, e.Y);
        
        _downPoint = (e.X, e.Y);
        
        // Don't hide icon if clicking on the icon itself
        // Check if click position is within icon window bounds
        if (_iconWindow != null && _iconWindow.IsVisible)
        {
            var iconPos = _iconWindow.Position;
            var iconBounds = new Avalonia.Rect(iconPos.X, iconPos.Y, 40, 40);
            if (iconBounds.Contains(new Avalonia.Point(e.X, e.Y)))
            {
                _logger.LogDebug("Click is on icon window, not hiding");
                return; // Don't hide - let the icon handle the click
            }
        }
        
        // Hide icon on any click elsewhere (start of new interaction)
        Dispatcher.UIThread.Post(() => HideIcon());
    }

    private void OnMouseUp(object? sender, SimpleMouseEventArgs e)
    {
        if (_configurationService.SelectionTranslation?.Enabled != true) return;
        if (_downPoint == null) return;

        var (x1, y1) = _downPoint.Value;
        var x2 = e.X;
        var y2 = e.Y;

        var distance = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
        
        _logger.LogDebug("Mouse Up at {X}, {Y}. Distance: {Distance}", x2, y2, distance);

        _downPoint = null;

        if (distance > DragThreshold)
        {
            _logger.LogInformation("Drag detected, getting selected text...");
            _lastIconX = x2;
            _lastIconY = y2;
            
            // Get selected text using UI Automation
            Task.Run(async () =>
            {
                try
                {
                    // Wait for potential selection finalization
                    await Task.Delay(50);
                    
                    // Backup clipboard (Must be on UI Thread)
                    var backup = await Dispatcher.UIThread.InvokeAsync(() => ClipboardHelper.BackupClipboardAsync(_logger));
                    
                    var text = await _platformService.GetSelectedTextAsync(x2, y2);
                    _lastSelectedText = text;
                    
                    // Restore clipboard (Must be on UI Thread)
                    await Dispatcher.UIThread.InvokeAsync(() => ClipboardHelper.RestoreClipboardAsync(backup, _logger));
                    
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        _logger.LogInformation("Got selected text: {Length} chars", text.Length);
                        // Show icon only if text is found
                        await Dispatcher.UIThread.InvokeAsync(() => ShowIcon(x2, y2));
                    }
                    else
                    {
                         _logger.LogDebug("No text selected (or extraction failed)");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting selected text");
                }
            });
        }
    }

    private void ShowIcon(int x, int y)
    {
        _logger.LogDebug("Showing icon at {X}, {Y}", x, y);
        
        if (_iconWindow == null)
        {
            _iconWindow = new SelectionIconWindow();
            _iconWindow.TranslateClicked += OnTranslateClicked;
        }
        
        // Ensure window is usable (in case it was closed externally)
        try 
        {
            // Set position
            var pixelPoint = new PixelPoint(x + 10, y + 10);
            _iconWindow.Position = pixelPoint;
            
            _iconWindow.Show();
            _iconWindow.Topmost = true;
            // DO NOT Activate() to avoid stealing focus
        }
        catch
        {
            // Recreate if failed (e.g. invalid handle)
            _iconWindow = new SelectionIconWindow();
            _iconWindow.TranslateClicked += OnTranslateClicked;
            _iconWindow.Position = new PixelPoint(x + 10, y + 10);
            _iconWindow.Show();
            _iconWindow.Topmost = true;
        }
        
        _logger.LogDebug("Icon window shown");
    }

    private void HideIcon()
    {
        try
        {
            _iconWindow?.Hide();
        }
        catch { /* Ignore */ }
    }

    private void OnTranslateClicked(object? sender, EventArgs e)
    {
        _logger.LogInformation("Translate icon clicked! Opening dialog...");
        
        // Get position and text before hiding
        var x = _lastIconX;
        var y = _lastIconY;
        var text = _lastSelectedText;
        
        // Open dialog first to maintain app focus/activation from the UI thread
        Dispatcher.UIThread.Post(async () => 
        {
            OpenTranslateDialog(x, y, text);
            
            // Short delay to ensure the new window is registered as the active window
            await Task.Delay(100);
            
            // Then close the icon
            HideIcon();
        });
    }
    
    private void OpenTranslateDialog(int x, int y, string? prefilledText)
    {
        _logger.LogInformation("Attempting to open translate dialog at {X}, {Y}", x, y);
        
        try
        {
            var dialog = new SelectionTranslateWindow();
            
            // Pre-fill text if we got it from UI Automation
            if (!string.IsNullOrEmpty(prefilledText))
            {
                dialog.SetSourceText(prefilledText);
            }
            
            // Position near where the icon was
            // Ensure we don't go off-screen (basic check)
            var screen = dialog.Screens.ScreenFromPoint(new PixelPoint(x, y)) ?? dialog.Screens.Primary;
            if (screen != null)
            {
                var screenRect = screen.WorkingArea;
                if (x + 450 > screenRect.Right) x = screenRect.Right - 470;
                if (y + 350 > screenRect.Bottom) y = screenRect.Bottom - 370;
            }

            dialog.Position = new PixelPoint(x + 20, y + 20);
            dialog.Show();
            // Don't activate to prevent focus theft
            // dialog.Activate();
            
            _logger.LogInformation("SelectionTranslateDialog opened successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open translate dialog. StackTrace: {StackTrace}", ex.StackTrace);
        }
    }

    public void Dispose()
    {
        _mouseHookService.MouseDown -= OnMouseDown;
        _mouseHookService.MouseUp -= OnMouseUp;
        _mouseHookService.Stop();
        if (_iconWindow != null)
        {
            _iconWindow.TranslateClicked -= OnTranslateClicked;
            _iconWindow.Close();
        }
    }
}
