using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Key = Avalonia.Input.Key;

using EasyChat.Models;

namespace EasyChat.Views.Overlay;

public enum OverlayMode
{
    Idle,
    Selecting,
    Resizing,
    Moving,
    Done
}

// ... (Enum ResizeHandle remains same)
public enum ResizeHandle
{
    None,
    TopLeft,
    TopCenter,
    TopRight,
    RightCenter,
    BottomRight,
    BottomCenter,
    BottomLeft,
    LeftCenter
}

public partial class OverlayWindowView : Window
{
    private readonly Bitmap _capturedImage;
    private readonly Rectangle _selectionRectangle;
    private readonly Border _hintBorder;
    private readonly TextBlock _hintTextBlock;
    private readonly Control _toolbarBorder; 
    private readonly Control _copyMenuBorder;
    private readonly Control _copyButton;
    private DispatcherTimer? _menuCloseTimer; 

    // Handles
    private readonly Border _handleTopLeft;
    private readonly Border _handleTopCenter;
    private readonly Border _handleTopRight;
    private readonly Border _handleRightCenter;
    private readonly Border _handleBottomRight;
    private readonly Border _handleBottomCenter;
    private readonly Border _handleBottomLeft;
    private readonly Border _handleLeftCenter;

    private Point _startPoint;
    private OverlayMode _currentMode = OverlayMode.Idle;
    private ResizeHandle _activeHandle = ResizeHandle.None;
    
    // For resizing
    private Rect _initialSelection;

    private readonly string _mode;
    private readonly CaptureIntent? _intent;

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
#pragma warning disable CS8618 
    public OverlayWindowView () {}
#pragma warning restore CS8618 

    public OverlayWindowView(PixelRect bounds, Bitmap capturedImage, string mode = Constants.Constant.ScreenshotMode.Quick, CaptureIntent? intent = null)
    {
        InitializeComponent();
        _capturedImage = capturedImage;
        _mode = mode;
        _intent = intent;

        // Window setup
        ShowInTaskbar = false;
        WindowState = WindowState.Normal;
        SystemDecorations = SystemDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        Topmost = true;

        Position = new PixelPoint(bounds.X, bounds.Y);
        
        _ = GetTopLevel(this)?.PlatformImpl;
        
        Width = bounds.Width; 
        Height = bounds.Height;
        
        Background = new ImageBrush(_capturedImage);

        _selectionRectangle = this.FindControl<Rectangle>("SelectionRectangle") ?? throw new InvalidOperationException("SelectionRectangle not found");
        _hintBorder = this.FindControl<Border>("HintBorder") ?? throw new InvalidOperationException("HintBorder not found");
        _hintTextBlock = this.FindControl<TextBlock>("HintTextBlock") ?? throw new InvalidOperationException("HintTextBlock not found");
        _toolbarBorder = this.FindControl<Control>("ToolbarBorder") ?? throw new InvalidOperationException("ToolbarBorder not found");
        _copyMenuBorder = this.FindControl<Control>("CopyMenuBorder") ?? throw new InvalidOperationException("CopyMenuBorder not found");
        _copyButton = this.FindControl<Control>("CopyButton") ?? throw new InvalidOperationException("CopyButton not found");

        _handleTopLeft = this.FindControl<Border>("HandleTopLeft")!;
        _handleTopCenter = this.FindControl<Border>("HandleTopCenter")!;
        _handleTopRight = this.FindControl<Border>("HandleTopRight")!;
        _handleRightCenter = this.FindControl<Border>("HandleRightCenter")!;
        _handleBottomRight = this.FindControl<Border>("HandleBottomRight")!;
        _handleBottomCenter = this.FindControl<Border>("HandleBottomCenter")!;
        _handleBottomLeft = this.FindControl<Border>("HandleBottomLeft")!;
        _handleLeftCenter = this.FindControl<Border>("HandleLeftCenter")!;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;

        if (_intent == CaptureIntent.RectSelection)
        {
             _hintTextBlock.Text = Lang.Resources.FixedArea_Hint;
        }
        else
        {
             _hintTextBlock.Text = Lang.Resources.Screenshot_Hint;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (GetCursorPos(out var lpPoint))
        {
            var pixelPoint = new PixelPoint(lpPoint.X, lpPoint.Y);
            var clientPoint = this.PointToClient(pixelPoint);
            HandleHint(clientPoint);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public event Action<Bitmap, CaptureIntent>? SelectionCompleted;
    public event Action<PixelRect>? RectSelected;
    public event Action? SelectionCanceled;


    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var position = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        if (props.IsRightButtonPressed)
        {
            Close();
            SelectionCanceled?.Invoke();
            return;
        }

        if (_currentMode == OverlayMode.Done)
        {
            // Check if clicking on a handle
            var handle = GetHitHandle(position);
            if (handle != ResizeHandle.None)
            {
                _currentMode = OverlayMode.Resizing;
                _activeHandle = handle;
                _startPoint = position;
                _initialSelection = new Rect(
                    Canvas.GetLeft(_selectionRectangle),
                    Canvas.GetTop(_selectionRectangle),
                    _selectionRectangle.Width,
                    _selectionRectangle.Height);
                
                _toolbarBorder.IsVisible = false; 
                return;
            }

            // Check if inside selection for Move
            var left = Canvas.GetLeft(_selectionRectangle);
            var top = Canvas.GetTop(_selectionRectangle);
            var rect = new Rect(left, top, _selectionRectangle.Width, _selectionRectangle.Height);

            if (rect.Contains(position))
            {
                _currentMode = OverlayMode.Moving;
                _startPoint = position;
                _initialSelection = rect;
                _toolbarBorder.IsVisible = false;
                HideHandles();
                Cursor = new Cursor(StandardCursorType.SizeAll);
                return;
            }
            
            // Click outside -> Start new selection
        }

        // Start new selection
        _currentMode = OverlayMode.Selecting;
        _activeHandle = ResizeHandle.None;
        
        _hintBorder.IsVisible = false;
        _toolbarBorder.IsVisible = false;
        HideHandles();

        _startPoint = position;

        _selectionRectangle.IsVisible = true;
        Canvas.SetLeft(_selectionRectangle, position.X);
        Canvas.SetTop(_selectionRectangle, position.Y);
        _selectionRectangle.Width = 0;
        _selectionRectangle.Height = 0;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition(this);

        if (_currentMode == OverlayMode.Idle)
        {
             HandleHint(position);
        }
        else if (_currentMode == OverlayMode.Selecting)
        {
            // ... (Selecting logic remains same)
            var x = Math.Min(position.X, _startPoint.X);
            var y = Math.Min(position.Y, _startPoint.Y);
            var width = Math.Abs(position.X - _startPoint.X);
            var height = Math.Abs(position.Y - _startPoint.Y);

            Canvas.SetLeft(_selectionRectangle, x);
            Canvas.SetTop(_selectionRectangle, y);
            _selectionRectangle.Width = width;
            _selectionRectangle.Height = height;
            
            UpdateHandles(x, y, width, height);
        }
        else if (_currentMode == OverlayMode.Resizing)
        {
            ResizeSelection(position);
        }
        else if (_currentMode == OverlayMode.Moving)
        {
            MoveSelection(position);
        }
        
        // Cursor updates for Idle/Done states
        if (_currentMode == OverlayMode.Done || _currentMode == OverlayMode.Idle)
        {
             UpdateCursor(position);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_currentMode == OverlayMode.Selecting || _currentMode == OverlayMode.Resizing || _currentMode == OverlayMode.Moving)
        {
            if (_selectionRectangle.Width > 0 && _selectionRectangle.Height > 0)
            {
                if (_mode == Constants.Constant.ScreenshotMode.Quick)
                {
                     ProcessSelection(_intent ?? CaptureIntent.Translation);
                     return;
                }

                _currentMode = OverlayMode.Done;
                ShowHandles();
                UpdateToolbarPosition();
                Cursor = Cursor.Default;
            }
            else
            {
                ResetSelection();
            }
        }
    }
    
    public void ConfirmButton_OnClick(object? sender, RoutedEventArgs e) => ProcessSelection(_intent ?? CaptureIntent.Translation);
    
    public void CopyOriginal_OnClick(object? sender, RoutedEventArgs e) => ProcessSelection(CaptureIntent.CopyOriginal);
    public void CopyTranslated_OnClick(object? sender, RoutedEventArgs e) => ProcessSelection(CaptureIntent.CopyTranslated);
    public void CopyBilingual_OnClick(object? sender, RoutedEventArgs e) => ProcessSelection(CaptureIntent.CopyBilingual);
    public void CopyImageTranslated_OnClick(object? sender, RoutedEventArgs e) => ProcessSelection(CaptureIntent.CopyImageTranslated);
    
    private void CopyButton_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        _menuCloseTimer?.Stop();
        
        // Position menu relative to button
        if (!_toolbarBorder.IsVisible) return;
        
        // Ensure layout is up to date
        var mainCanvas = this.FindControl<Canvas>("MainCanvas");
        if (mainCanvas == null) return;
        var buttonPos = _copyButton.TranslatePoint(new Point(0,0), mainCanvas);
        
        if (buttonPos.HasValue)
        {
             Canvas.SetLeft(_copyMenuBorder, buttonPos.Value.X);
             // Place below toolbar. Toolbar is at Canvas.GetTop(_toolbarBorder). 
             // We can just use buttonPos.Y + button Height.
             // But button height might be implicit.
             // Toolbar height is better known or relative.
             
             // Safer: Get screen coords of button bottom-left?
             // Since everything is in MainCanvas...
             
             Canvas.SetTop(_copyMenuBorder, Canvas.GetTop(_toolbarBorder) + _toolbarBorder.Bounds.Height + 5);
             
             // If toolbar is placed ABOVE selection (near top edge of screen), the menu should be BELOW toolbar?
             // Or if toolbar is inside-bottom?
             
             // Dynamic placement:
             var tbTop = Canvas.GetTop(_toolbarBorder);
             var tbHeight = _toolbarBorder.Bounds.Height;
             var menuTop = tbTop + tbHeight + 2;
             
             // Check if menu overflows bottom
             if (menuTop + 150 > this.Height) // approx menu height
             {
                 // Place above
                 menuTop = tbTop - 150 - 2;
             }
             
             Canvas.SetTop(_copyMenuBorder, menuTop);
             _copyMenuBorder.IsVisible = true;
        }
    }
    
    private void CopyButton_OnPointerExited(object? sender, PointerEventArgs e)
    {
        StartMenuCloseTimer();
    }
    
    private void CopyMenu_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        _menuCloseTimer?.Stop();
    }
    
    private void CopyMenu_OnPointerExited(object? sender, PointerEventArgs e)
    {
        StartMenuCloseTimer();
    }
    
    private void StartMenuCloseTimer()
    {
        _menuCloseTimer?.Stop();
        _menuCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _menuCloseTimer.Tick += (_, _) =>
        {
            _copyMenuBorder.IsVisible = false;
            _menuCloseTimer.Stop();
        };
        _menuCloseTimer.Start();
    }
    
    public void ResetButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ResetSelection();
    }

    public void CancelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
        SelectionCanceled?.Invoke();
    }
    
    private void MoveSelection(Point currentPos)
    {
        double dx = currentPos.X - _startPoint.X;
        double dy = currentPos.Y - _startPoint.Y;
        
        double newX = _initialSelection.X + dx;
        double newY = _initialSelection.Y + dy;
        
        // Optional: Bounds check to keep inside window (simple check)
        // if (newX < 0) newX = 0;
        // if (newY < 0) newY = 0;
        // if (newX + _initialSelection.Width > Width) newX = Width - _initialSelection.Width;
        // if (newY + _initialSelection.Height > Height) newY = Height - _initialSelection.Height;

        Canvas.SetLeft(_selectionRectangle, newX);
        Canvas.SetTop(_selectionRectangle, newY);
        
        UpdateHandles(newX, newY, _initialSelection.Width, _initialSelection.Height);
    }

    private void ResetSelection()
    {
        _currentMode = OverlayMode.Idle;
        _selectionRectangle.IsVisible = false;
        HideHandles();
        _toolbarBorder.IsVisible = false;
        _copyMenuBorder.IsVisible = false;
        _activeHandle = ResizeHandle.None;
        Cursor = Cursor.Default;
        _hintBorder.IsVisible = true; // Show hint again
    }

    private void UpdateCursor(Point p)
    {
        if (_selectionRectangle.IsVisible)
        {
            var handle = GetHitHandle(p);
            if (handle != ResizeHandle.None)
            {
                 // Set based on handle
                 switch (handle)
                 {
                     case ResizeHandle.TopLeft: Cursor = new Cursor(StandardCursorType.TopLeftCorner); break;
                     case ResizeHandle.TopCenter: Cursor = new Cursor(StandardCursorType.SizeNorthSouth); break;
                     case ResizeHandle.TopRight: Cursor = new Cursor(StandardCursorType.TopRightCorner); break;
                     case ResizeHandle.RightCenter: Cursor = new Cursor(StandardCursorType.SizeWestEast); break;
                     case ResizeHandle.BottomRight: Cursor = new Cursor(StandardCursorType.BottomRightCorner); break;
                     case ResizeHandle.BottomCenter: Cursor = new Cursor(StandardCursorType.SizeNorthSouth); break;
                     case ResizeHandle.BottomLeft: Cursor = new Cursor(StandardCursorType.BottomLeftCorner); break;
                     case ResizeHandle.LeftCenter: Cursor = new Cursor(StandardCursorType.SizeWestEast); break;
                 }
                 return;
            }
            
            var left = Canvas.GetLeft(_selectionRectangle);
            var top = Canvas.GetTop(_selectionRectangle);
            var rect = new Rect(left, top, _selectionRectangle.Width, _selectionRectangle.Height);
            if (rect.Contains(p))
            {
                Cursor = new Cursor(StandardCursorType.SizeAll);
                return;
            }
        }
        Cursor = Cursor.Default;
    }

    // ... (ResizeSelection, UpdateHandles, SetHandle, ShowHandles, HideHandles, GetHitHandle, HitTest, HandleHint remain same)


    private void ResizeSelection(Point currentPos)
    {
        double x = _initialSelection.X;
        double y = _initialSelection.Y;
        double w = _initialSelection.Width;
        double h = _initialSelection.Height;
        
        double dx = currentPos.X - _startPoint.X;
        double dy = currentPos.Y - _startPoint.Y;

        switch (_activeHandle)
        {
            case ResizeHandle.TopLeft:
                x += dx; y += dy; w -= dx; h -= dy;
                break;
            case ResizeHandle.TopCenter:
                y += dy; h -= dy;
                break;
            case ResizeHandle.TopRight:
                y += dy; w += dx; h -= dy;
                break;
            case ResizeHandle.RightCenter:
                w += dx;
                break;
            case ResizeHandle.BottomRight:
                w += dx; h += dy;
                break;
            case ResizeHandle.BottomCenter:
                h += dy;
                break;
            case ResizeHandle.BottomLeft:
                x += dx; w -= dx; h += dy;
                break;
            case ResizeHandle.LeftCenter:
                x += dx; w -= dx;
                break;
        }

        // Constraints to prevent flipping (simplification)
        if (w < 1) w = 1;
        if (h < 1) h = 1;

        Canvas.SetLeft(_selectionRectangle, x);
        Canvas.SetTop(_selectionRectangle, y);
        _selectionRectangle.Width = w;
        _selectionRectangle.Height = h;
        
        UpdateHandles(x, y, w, h);
    }

    private void UpdateHandles(double x, double y, double w, double h)
    {
        if (!_selectionRectangle.IsVisible) return;

        SetHandle(_handleTopLeft, x - 5, y - 5);
        SetHandle(_handleTopCenter, x + w / 2 - 5, y - 5);
        SetHandle(_handleTopRight, x + w - 5, y - 5);
        
        SetHandle(_handleRightCenter, x + w - 5, y + h / 2 - 5);
        
        SetHandle(_handleBottomRight, x + w - 5, y + h - 5);
        SetHandle(_handleBottomCenter, x + w / 2 - 5, y + h - 5);
        SetHandle(_handleBottomLeft, x - 5, y + h - 5);
        
        SetHandle(_handleLeftCenter, x - 5, y + h / 2 - 5);
    }

    private void SetHandle(Border handle, double x, double y)
    {
        Canvas.SetLeft(handle, x);
        Canvas.SetTop(handle, y);
    }

    private void ShowHandles()
    {
        _handleTopLeft.IsVisible = true;
        _handleTopCenter.IsVisible = true;
        _handleTopRight.IsVisible = true;
        _handleRightCenter.IsVisible = true;
        _handleBottomRight.IsVisible = true;
        _handleBottomCenter.IsVisible = true;
        _handleBottomLeft.IsVisible = true;
        _handleLeftCenter.IsVisible = true;
    }

    private void HideHandles()
    {
        _handleTopLeft.IsVisible = false;
        _handleTopCenter.IsVisible = false;
        _handleTopRight.IsVisible = false;
        _handleRightCenter.IsVisible = false;
        _handleBottomRight.IsVisible = false;
        _handleBottomCenter.IsVisible = false;
        _handleBottomLeft.IsVisible = false;
        _handleLeftCenter.IsVisible = false;
    }

    private ResizeHandle GetHitHandle(Point p)
    {
        if (HitTest(_handleTopLeft, p)) return ResizeHandle.TopLeft;
        if (HitTest(_handleTopCenter, p)) return ResizeHandle.TopCenter;
        if (HitTest(_handleTopRight, p)) return ResizeHandle.TopRight;
        if (HitTest(_handleRightCenter, p)) return ResizeHandle.RightCenter;
        if (HitTest(_handleBottomRight, p)) return ResizeHandle.BottomRight;
        if (HitTest(_handleBottomCenter, p)) return ResizeHandle.BottomCenter;
        if (HitTest(_handleBottomLeft, p)) return ResizeHandle.BottomLeft;
        if (HitTest(_handleLeftCenter, p)) return ResizeHandle.LeftCenter;
        return ResizeHandle.None;
    }

    private bool HitTest(Border target, Point p)
    {
        if (!target.IsVisible) return false;
        var left = Canvas.GetLeft(target);
        var top = Canvas.GetTop(target);
        var rect = new Rect(left, top, target.Width, target.Height);
        // Add padding for easier grabbing
        return rect.Inflate(5).Contains(p);
    }

    private void HandleHint(Point position)
    {
        // ... (Keep existing Logic for HintBorder or simplify)
        // For brevity in this refactor, I'll port over the essential parts:
        
        var currentPixelPoint = this.PointToScreen(position);
        var screen = Screens.ScreenFromPoint(currentPixelPoint);

        if (screen != null)
        {
            var screenTopLeftPixel = screen.Bounds.Position;
            var targetPixel = new PixelPoint(screenTopLeftPixel.X + 30, screenTopLeftPixel.Y + 30);
            var targetPoint = this.PointToClient(targetPixel);

            Canvas.SetLeft(_hintBorder, targetPoint.X);
            Canvas.SetTop(_hintBorder, targetPoint.Y);
            
            var hintRect = new Rect(targetPoint.X, targetPoint.Y, _hintBorder.Bounds.Width, _hintBorder.Bounds.Height);
            _hintBorder.IsVisible = !hintRect.Contains(position);
        }
    }
    
    private void UpdateToolbarPosition()
    {
        var x = Canvas.GetLeft(_selectionRectangle);
        var y = Canvas.GetTop(_selectionRectangle);
        var width = _selectionRectangle.Width;
        var height = _selectionRectangle.Height;

        // Force measure to get accurate size
        if (_toolbarBorder.Bounds.Width == 0 || _toolbarBorder.Bounds.Height == 0)
        {
             _toolbarBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
             _toolbarBorder.Arrange(new Rect(_toolbarBorder.DesiredSize));
        }
        
        var b = _toolbarBorder.Bounds;
        var tbHeight = b.Height > 0 ? b.Height : 60; 
        var tbWidth = b.Width > 0 ? b.Width : 200;

        // Determine limits based on the screen where the selection is
        double limitLeft = 0;
        double limitRight = this.Bounds.Width;
        double limitBottom = this.Bounds.Height;
        
        // Hide Copy Button if doing Rect Selection (only Confirm/Cancel/Reset needed)
        if (_intent == CaptureIntent.RectSelection)
        {
            _copyButton.IsVisible = false;
        }
        else
        {
            _copyButton.IsVisible = true;
        }
        // double limitTop = 0; // Not strictly used for bottom check logic but good to know

        try 
        {
            // Use bottom-left of selection to find the screen (or center-bottom)
            var targetPoint = new Point(x + width / 2, y + height);
            var screenPoint = this.PointToScreen(targetPoint);
            var screen = Screens.ScreenFromPoint(screenPoint);
            
            if (screen != null)
            {
                var workingArea = screen.WorkingArea;
                var topLeft = this.PointToClient(workingArea.Position); // Screen Pixels -> Client Logical
                var bottomRight = this.PointToClient(new PixelPoint(workingArea.X + workingArea.Width, workingArea.Y + workingArea.Height));
                
                limitLeft = topLeft.X;
                limitRight = bottomRight.X;
                limitBottom = bottomRight.Y;
                // limitTop = topLeft.Y; 
            }
        }
        catch (Exception) { /* Fallback to window bounds */ }

        var finalX = x + width - tbWidth;
        
        // Horizontal clamping within screen working area
        if (finalX + tbWidth > limitRight) finalX = limitRight - tbWidth - 10;
        if (finalX < limitLeft) finalX = limitLeft + 10;

        // Vertical positioning
        var finalY = y + height + 10;
        
        // Check overflow against working area bottom
        if (finalY + tbHeight > limitBottom)
        {
            // Flip above
            finalY = y - tbHeight - 10;
            
            // If overflowing top of screen (or selection top < toolbar height)
            // Just checking < 0 or < limitTop might be enough, but let's stick to simple logic first
            // If flipped position is "too high" relative to selection? 
            // The user wants it VISIBLE.
            
            // If even flipped it's off screen (e.g. huge selection filling height)
            if (finalY < 0) 
            {
                // Place inside-bottom
                finalY = y + height - tbHeight - 10;
                
                // If inside-bottom is ALSO "above" the selection start (super tiny selection?)
                // Just clamp to stay within selection/screen
                // Priority: Keep inside selection
            }
        }
         
         Canvas.SetLeft(_toolbarBorder, finalX);
         Canvas.SetTop(_toolbarBorder, finalY);
         _toolbarBorder.IsVisible = true;
    }


    private void ProcessSelection(CaptureIntent intent)
    {
        _copyMenuBorder.IsVisible = false; // Added this line
        try 
        {
            var xRaw = Canvas.GetLeft(_selectionRectangle);
            var yRaw = Canvas.GetTop(_selectionRectangle);
            var widthRaw = _selectionRectangle.Width;
            var heightRaw = _selectionRectangle.Height;
            
            var scaling = RenderScaling;

            var x = xRaw * scaling;
            var y = yRaw * scaling;
            var width = widthRaw * scaling;
            var height = heightRaw * scaling;

            _selectionRectangle.IsVisible = false;
            _toolbarBorder.IsVisible = false;
            HideHandles();

            if (width > 0 && height > 0)
            {
                if (intent == CaptureIntent.RectSelection)
                {
                     RectSelected?.Invoke(new PixelRect((int)x, (int)y, (int)width, (int)height));
                }
                else
                {
                    var selectedRegion = new CroppedBitmap(_capturedImage,
                        new PixelRect((int)x, (int)y, (int)width, (int)height));

                    var bitmap = RenderCroppedBitmap(selectedRegion);

                    SelectionCompleted?.Invoke(bitmap, intent);
                }
            }
            else
            {
                SelectionCanceled?.Invoke();
            }

            Close();
        }
        catch (Exception ex)
        {
             Console.WriteLine(ex);
             SelectionCanceled?.Invoke();
             Close();
        }
    }
    
    // ... InputElement_OnKeyDown and RenderCroppedBitmap ...
    private void InputElement_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            SelectionCanceled?.Invoke();
        }
        else if (e.Key == Key.Enter)
        {
             if (_toolbarBorder.IsVisible)
             {
                 ProcessSelection(CaptureIntent.Translation);
             }
        }
    }

    private Bitmap RenderCroppedBitmap(CroppedBitmap source)
    {
        var pixelSize = new PixelSize((int)source.Size.Width, (int)source.Size.Height);
        var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            ctx.DrawImage(source, new Rect(source.Size));
        }
        return bitmap;
    }
    
    private void TopLevel_OnClosed(object? sender, EventArgs e) {}
}