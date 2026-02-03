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
    
    private SelectionIconWindowView? _iconWindow;
    private TranslationDictionaryWindowView? _currentTranslateWindow;
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
        _mouseHookService.MouseDoubleClick += OnMouseDoubleClick;
        
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
            var iconBounds = new Rect(iconPos.X, iconPos.Y, 40, 40);
            if (iconBounds.Contains(new Point(e.X, e.Y)))
            {
                _logger.LogDebug("Click is on icon window, not hiding");
                return; // Don't hide - let the icon handle the click
            }
        }
        
        // Check if click is inside the Translation Window (if open)
        if (_currentTranslateWindow != null && _currentTranslateWindow.IsVisible)
        {
            var screenPoint = new PixelPoint(e.X, e.Y);
            var clientPoint = _currentTranslateWindow.PointToClient(screenPoint);
            var bounds = new Rect(0, 0, _currentTranslateWindow.Bounds.Width, _currentTranslateWindow.Bounds.Height);
            
            if (bounds.Contains(clientPoint))
            {
                 _logger.LogDebug("Click is inside Translation Window, ignoring.");
                 return;
            }
            
            // Click is outside -> Close Window
            Dispatcher.UIThread.Post(() => 
            {
                try { _currentTranslateWindow?.Close(); }
                catch
                {
                    // ignored
                }
            });
        }
        
        // Hide icon on any click elsewhere (start of new interaction)
        
        // Check if this MouseDown is part of a recent Double Click sequence.
        // If so, we should IGNORE it to avoid cancelling the Double Click operation.
        if ((DateTime.Now - _lastDoubleClickTime).TotalMilliseconds < 500)
        {
             // Check distance - if close, it's likely the 2nd click of the double click or a ghost click
             var dist = Math.Sqrt(Math.Pow(e.X - _lastIconX, 2) + Math.Pow(e.Y - _lastIconY, 2));
             if (dist < 40) // generous tolerance for double click movement
             {
                  _logger.LogDebug("Ignoring MouseDown near DoubleClick (Time: {Time}ms, Dist: {Dist})", 
                      (DateTime.Now - _lastDoubleClickTime).TotalMilliseconds, dist);
                  return; 
             }
        }

        UpdateGeneration();
        Dispatcher.UIThread.Post(() => HideIcon());
    }

    private long _interactionGeneration;
    private DateTime _lastDoubleClickTime;

    private void UpdateGeneration()
    {
        System.Threading.Interlocked.Increment(ref _interactionGeneration);
    }

    private void HideIcon()
    {
        try
        {
            // Generation update matches OnMouseDown's sync call, but we do UI cleanup here
             _iconWindow?.Close();
             _iconWindow = null;
        }
        catch { /* Ignore */ }
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
            
            // Capture current generation
            var gen = System.Threading.Interlocked.Read(ref _interactionGeneration);
            
            // Get selected text using UI Automation
            Task.Run(async () =>
            {
                try
                {
                    // Wait for potential selection finalization
                    await Task.Delay(50);
                    
                    // Check if canceled
                    if (gen != System.Threading.Interlocked.Read(ref _interactionGeneration)) return;
                    
                    // Backup clipboard (Must be on UI Thread)
                    var backup = await Dispatcher.UIThread.InvokeAsync(() => ClipboardHelper.BackupClipboardAsync(_logger));
                    
                    var text = await _platformService.GetSelectedTextAsync(x2, y2);
                    _lastSelectedText = text;
                    
                    // Restore clipboard (Must be on UI Thread)
                    await Dispatcher.UIThread.InvokeAsync(() => ClipboardHelper.RestoreClipboardAsync(backup, _logger));
                    
                    // Check if canceled again before showing
                    if (gen != System.Threading.Interlocked.Read(ref _interactionGeneration)) return;

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        _logger.LogInformation("Got selected text: {Length} chars", text.Length);
                        // Show icon only if text is found
                        await Dispatcher.UIThread.InvokeAsync(() => 
                        {
                            if (gen == System.Threading.Interlocked.Read(ref _interactionGeneration))
                                ShowIcon(x2, y2);
                        });
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

    private void OnMouseDoubleClick(object? sender, SimpleMouseEventArgs e)
    {
        if (_configurationService.SelectionTranslation?.Enabled != true) return;
        
        _logger.LogInformation("Double Click detected at {X}, {Y}", e.X, e.Y);
        
        _lastDoubleClickTime = DateTime.Now;
        _lastIconX = e.X;
        _lastIconY = e.Y;
            
        var gen = System.Threading.Interlocked.Read(ref _interactionGeneration);

        // Get selected text using UI Automation
        Task.Run(async () =>
        {
            try
            {
                // Wait for potential selection finalization (double click selects word)
                // Increased delay to ensure OS highlights text
                await Task.Delay(150);
                
                var currentGen = System.Threading.Interlocked.Read(ref _interactionGeneration);
                if (gen != currentGen) 
                {
                    _logger.LogDebug("Double Click cancelled. Gen mismatch: {Captured} != {Current}", gen, currentGen);
                    return;
                }
                    
                // Backup clipboard (Must be on UI Thread)
                var backup = await Dispatcher.UIThread.InvokeAsync(() => ClipboardHelper.BackupClipboardAsync(_logger));
                    
                var text = await _platformService.GetSelectedTextAsync(e.X, e.Y);
                _lastSelectedText = text;
                    
                // Restore clipboard (Must be on UI Thread)
                await Dispatcher.UIThread.InvokeAsync(() => ClipboardHelper.RestoreClipboardAsync(backup, _logger));
                    
                if (gen != System.Threading.Interlocked.Read(ref _interactionGeneration)) return;

                if (!string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogInformation("Got selected text (Double Click): {Length} chars", text.Length);
                    // Show icon only if text is found
                    await Dispatcher.UIThread.InvokeAsync(() => 
                    {
                        if (gen == System.Threading.Interlocked.Read(ref _interactionGeneration))
                            ShowIcon(e.X, e.Y);
                    });
                }
                else
                {
                     _logger.LogDebug("No text selected (Double Click) - Text was empty");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting selected text (Double Click)");
            }
        });
    }

    private void ShowIcon(int x, int y)
    {
        _logger.LogDebug("Showing icon at {X}, {Y}", x, y);
        
        if (_iconWindow == null)
        {
            _iconWindow = new SelectionIconWindowView();
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
            _iconWindow = new SelectionIconWindowView();
            _iconWindow.TranslateClicked += OnTranslateClicked;
            _iconWindow.Position = new PixelPoint(x + 10, y + 10);
            _iconWindow.Show();
            _iconWindow.Topmost = true;
        }
        
        _logger.LogDebug("Icon window shown");
    }

    private void OnTranslateClicked(object? sender, EventArgs e)
    {
        _logger.LogInformation("Translate icon clicked! Opening dialog...");
        
        // Get position and text before any async operation
        var x = _lastIconX;
        var y = _lastIconY;
        var text = _lastSelectedText;
        
        var gen = System.Threading.Interlocked.Read(ref _interactionGeneration);

        // Immediately show loading spinner on UI thread
        Dispatcher.UIThread.Post(() => _iconWindow?.ShowLoading());
        
        // Run the preparation asynchronously to avoid blocking
        Task.Run(async () =>
        {
            try
            {
                // Check cancellation
                if (gen != System.Threading.Interlocked.Read(ref _interactionGeneration)) return;

                // Create and prepare the dialog on the UI thread
                TranslationDictionaryWindowView? dialog = null;
                
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (gen != System.Threading.Interlocked.Read(ref _interactionGeneration)) return;

                    _logger.LogInformation("Attempting to open translate dialog at {X}, {Y}", x, y);
                    
                    // Close existing window if any (Singleton behavior)
                    try { _currentTranslateWindow?.Close(); } catch { /* Ignore if already closing */ }

                    dialog = new TranslationDictionaryWindowView();
                    _currentTranslateWindow = dialog;
                    
                    // Handle cleanup when closed manually
                    dialog.Closed += (_, _) => 
                    {
                        if (_currentTranslateWindow == dialog)
                        {
                            _currentTranslateWindow = null;
                        }
                    };
                });
                
                if (gen != System.Threading.Interlocked.Read(ref _interactionGeneration)) return;

                if (dialog == null || string.IsNullOrEmpty(text))
                {
                    // Fallback: show dialog without async init
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (gen != System.Threading.Interlocked.Read(ref _interactionGeneration)) return;

                        if (dialog != null && !string.IsNullOrEmpty(text))
                        {
                            dialog.SetSourceText(text);
                        }
                        ShowDialogAtPosition(dialog, x, y);
                        HideIconAndLoading();
                    });
                    return;
                }
                
                // Initialize asynchronously - this does the heavy lifting
                await dialog.InitializeAsync(text);
                
                if (gen != System.Threading.Interlocked.Read(ref _interactionGeneration)) return;
                
                // Now show the prepared dialog on UI thread
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (gen != System.Threading.Interlocked.Read(ref _interactionGeneration)) return;

                    ShowDialogAtPosition(dialog, x, y);
                    HideIconAndLoading();
                    _logger.LogInformation("SelectionTranslateDialog opened successfully");
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open translate dialog. StackTrace: {StackTrace}", ex.StackTrace);
                await Dispatcher.UIThread.InvokeAsync(HideIconAndLoading);
            }
        });
    }
    
    private void ShowDialogAtPosition(TranslationDictionaryWindowView? dialog, int x, int y)
    {
        if (dialog == null) return;
        
        // Window dimensions (approximate or max)
        const int windowWidth = 450;
        const int estimatedHeight = 350; // Use a reasonable estimate or the max height
        
        // Offset from cursor
        const int offset = 20;

        // Default Position: Bottom-Right
        var finalX = x + offset;
        var finalY = y + offset;
        
        var screen = dialog.Screens.ScreenFromPoint(new PixelPoint(x, y)) ?? dialog.Screens.Primary;
        if (screen != null)
        {
            var screenRect = screen.WorkingArea;
            
            // --- Horizontal Logic ---
            // Check if Right overflow
            if (finalX + windowWidth > screenRect.Right)
            {
                // Try Left: Cursor X - Width - Offset
                var leftX = x - windowWidth - offset;
                
                // If Left fits, use it
                if (leftX >= screenRect.X)
                {
                    finalX = leftX;
                }
                else
                {
                    // Neither fits perfectly. Choose the side with MORE space? 
                    // Or just clamp the Right version. 
                    // Let's stick to Clamping the Right version for now, as it's safer.
                    finalX = screenRect.Right - windowWidth - 10; // 10px padding from edge
                }
            }

            // --- Vertical Logic ---
            // Check if Bottom overflow
            if (finalY + estimatedHeight > screenRect.Bottom)
            {
                // Try Top: Cursor Y - Height - Offset
                var topY = y - estimatedHeight - offset;
                
                // If Top fits, use it
                if (topY >= screenRect.Y)
                {
                    finalY = topY;
                }
                else
                {
                    // Vertical Clamp
                    finalY = screenRect.Bottom - estimatedHeight - 10;
                }
            }
            
            // Final Safety Clamp (Absolute Bounds)
            if (finalX < screenRect.X) finalX = screenRect.X;
            if (finalY < screenRect.Y) finalY = screenRect.Y;
        }

        dialog.Position = new PixelPoint(finalX, finalY);
        dialog.Show();
        // Don't activate to prevent focus theft
        // dialog.Activate();
    }
    
    private void HideIconAndLoading()
    {
        _iconWindow?.HideLoading();
        HideIcon();
    }

    public async Task TranslateCurrentSelectionAsync()
    {
        var (x, y) = _platformService.GetCursorPosition();
        _logger.LogInformation("Shortcut Translate at {X}, {Y}", x, y);

        // Backup clipboard (Must be on UI Thread)
        var backup = await Dispatcher.UIThread.InvokeAsync(() => ClipboardHelper.BackupClipboardAsync(_logger));

        // Get text
        var text = await _platformService.GetSelectedTextAsync(x, y);

        // Restore clipboard (Must be on UI Thread)
        await Dispatcher.UIThread.InvokeAsync(() => ClipboardHelper.RestoreClipboardAsync(backup, _logger));

        if (string.IsNullOrWhiteSpace(text)) return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
             // Close existing (Singleton)
            try { _currentTranslateWindow?.Close(); } catch { /* Ignore */ }

            var dialog = new TranslationDictionaryWindowView();
            _currentTranslateWindow = dialog;
            
            dialog.Closed += (_, _) => 
            {
                if (_currentTranslateWindow == dialog) _currentTranslateWindow = null;
            };

            // Start initialization (loading state)
            // We just fire off the task, the VM handles the async translation and updates UI
            _ = dialog.InitializeAsync(text);
            
            ShowDialogAtPosition(dialog, x, y);
            
            _logger.LogInformation("Opened translation window via shortcut");
        });
    }

    public void Dispose()
    {
        _mouseHookService.MouseDown -= OnMouseDown;
        _mouseHookService.MouseUp -= OnMouseUp;
        _mouseHookService.MouseDoubleClick -= OnMouseDoubleClick;
        _mouseHookService.Stop();
        if (_iconWindow != null)
        {
            _iconWindow.TranslateClicked -= OnTranslateClicked;
            _iconWindow.Close();
        }
    }
}
