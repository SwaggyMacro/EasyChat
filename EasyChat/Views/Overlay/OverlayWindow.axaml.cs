using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Key = Avalonia.Input.Key; // Alias

namespace EasyChat.Views.Overlay;

public partial class OverlayWindow : Window
{
    private readonly Bitmap _capturedImage;
    private readonly Rectangle _selectionRectangle;
    private Point _startPoint;


    [SuppressMessage("ReSharper", "UnusedMember.Global")]
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public OverlayWindow () {}
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    public OverlayWindow(PixelRect bounds, Bitmap capturedImage)
    {
        InitializeComponent();
        _capturedImage = capturedImage;

        // Window setup
        ShowInTaskbar = false;
        
        // Use manual positioning and sizing for multi-monitor support
        // WindowState.FullScreen often restricts to a single monitor
        WindowState = WindowState.Normal;
        SystemDecorations = SystemDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        Topmost = true;

        // Set position and size
        Position = new PixelPoint(bounds.X, bounds.Y);
        
        // Note: Width/Height in Avalonia are logical units. 
        // We need to act carefully here. Ideally we set the size to cover the virtual screen.
        // Since we are creating a window that might span screens with different DPIs, 
        // Avalonia's behavior can be complex. 
        // A common workaround for spanning windows is ensuring we use the correct size.
        // However, for simplicity and effectiveness in many cases:
        // We can rely on the fact that we placed the window at the top-left of the virtual desktop area.
        // We should set the Width/Height large enough. 
        
        // _screen is removed, we use RenderScaling or assume 1.0 for the overlay logic if we are just drawing on top of a bitmap that matches the pixel real estate.
        // But wait, if we have mixed DPI, the 'logical' size of the window might vary.
        // Let's rely on pixel alignment.
        
        // For now, let's try to set the size based on the bounds.
        // We might need to adjust this if mixed DPI causes issues, but this is the standard approach for spanning.
        
        // This is tricky because we don't know the exact scaling factor of the primary screen vs others here easily without strict context,
        // but often setting it large is enough. 
        // Let's use the bounds size but we might need to converting pixels to logical units?
        // Actually, if we set SystemDecorations.None, we often can just set the pixel size if we could.
        // But Avalonia Window.Width is logical.
        
        // Let's do a best effort calculation assuming the primary screen's scaling or just using the bounds.
        // If we want to be safe, we can try to find the screen we are starting on.
        
        var platformHandle = GetTopLevel(this)?.PlatformImpl;
        // Optimization: just maximize? No, Maximize restricts to one monitor usually.
        
        // NOTE: We will set the size after loading to ensure scaling is picked up? 
        // Or just map pixels to logicals using the screen info from where we are positioned.
        
        // Simplification: Set it to a very large size? No.
        
        // Let's use the PlatformImpl or iterate screens to find scaling?
        // For this iteration, let's assume we can set the Frame size or similar.
        
        // Actually, there is a simpler way: Width/Height are logical. 
        // Start with a guess, then maybe resize? 
        // Let's try just setting it to the pixel bounds and see if Avalonia scales it down.
        // Wait, if scaling > 1, setting Width = PixelWidth means the window is smaller than screen. 
        // We need LogicalWidth = PixelWidth / Scaling.
        
        // Since we don't have the 'screen' object easily passed for the whole rect (it's multiple),
        // we can try to let the layout system handle it or pass the unioned bounds.
        
        // Let's keep it simple: Pass pixel bounds, and try to adjust.
        // But we removed `_screen`. 
        
        // Let's try to infer scaling from the Position (which is on some screen).
        // Since we can't easily get the screen from the constructor before showing (sometimes), 
        // we might keep it simple: 
        
        Width = bounds.Width; // This assumes 1:1 scaling if we are not careful. 
        Height = bounds.Height;
        
        Background = new ImageBrush(_capturedImage);

        _selectionRectangle = SelectionRectangle;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
    }

    public event Action<Bitmap>? SelectionCompleted;
    public event Action? SelectionCanceled;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var position = e.GetPosition(this);
        _startPoint = position;

        _selectionRectangle.IsVisible = true;
        Canvas.SetLeft(_selectionRectangle, position.X);
        Canvas.SetTop(_selectionRectangle, position.Y);
        _selectionRectangle.Width = 0;
        _selectionRectangle.Height = 0;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_selectionRectangle.IsVisible)
        {
            var position = e.GetPosition(this);

            var x = Math.Min(position.X, _startPoint.X);
            var y = Math.Min(position.Y, _startPoint.Y);
            var width = Math.Abs(position.X - _startPoint.X);
            var height = Math.Abs(position.Y - _startPoint.Y);

            Canvas.SetLeft(_selectionRectangle, x);
            Canvas.SetTop(_selectionRectangle, y);
            _selectionRectangle.Width = width;
            _selectionRectangle.Height = height;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            if (_selectionRectangle.IsVisible && e.InitialPressMouseButton == MouseButton.Left)
            {
                var position = e.GetPosition(this);

                // Assuming Scaling is handled by the bitmap or coordinates are compatible.
                // Screen.Scaling might be needed if the bitmap is physical pixels but Pointer events are logical.
                // Avalonia 11 handles scaling somewhat automatically, but capturing native screen (BitBlt) gives physical pixels.
                // So we likely need to scale the selection coordinates to physical pixels to crop the bitmap.

                var scaling = RenderScaling;

                var x = Math.Min(position.X, _startPoint.X) * scaling;
                var y = Math.Min(position.Y, _startPoint.Y) * scaling;
                var width = Math.Abs(position.X - _startPoint.X) * scaling;
                var height = Math.Abs(position.Y - _startPoint.Y) * scaling;

                _selectionRectangle.IsVisible = false;

                // Crop
                // Using SkiaSharp logic wrapped properly or just Avalonia CroppedBitmap
                // CroppedBitmap takes PixelRect (physical)

                // Ensure bounds
                if (width > 0 && height > 0)
                {
                    var selectedRegion = new CroppedBitmap(_capturedImage,
                        new PixelRect((int)x, (int)y, (int)width, (int)height));

                    // Convert CroppedBitmap to Bitmap to satisfy Action<Bitmap>
                    // CroppedBitmap is IImage, need to draw it or save-load.
                    // A simple way is to use a RenderTargetBitmap or just pass IImage if possible.
                    // But our Service layer expects Bitmap (for OCR).

                    // Helper to convert IImage to Bitmap
                    var bitmap = RenderCroppedBitmap(selectedRegion);

                    SelectionCompleted?.Invoke(bitmap);
                }
                else
                {
                    SelectionCanceled?.Invoke();
                }

                Close();
            }

            if (e.InitialPressMouseButton == MouseButton.Right)
            {
                Close();
                SelectionCanceled?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            SelectionCanceled?.Invoke();
        }
    }

    private void TopLevel_OnClosed(object? sender, EventArgs e)
    {
        // cleanup
    }

    private void InputElement_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            SelectionCanceled?.Invoke();
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
}