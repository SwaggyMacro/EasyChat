using System;
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
    private readonly Screen _screen;
    private readonly Rectangle _selectionRectangle;
    private Point _startPoint;

    // We pass IScreenCaptureService here, or pre-captured bitmap.
    // Passing Pre-captured bitmap is faster if we captured it in the session manager.
    // Let's pass the captured bitmap to avoid double capture or delay in constructor.
    public OverlayWindow(Screen screen, Bitmap capturedImage)
    {
        InitializeComponent();
        _screen = screen;
        _capturedImage = capturedImage;

        // Window setup
        ShowInTaskbar = false;
        Width = screen.Bounds.Width;
        Height = screen.Bounds.Height;
        Topmost = true;
        Position = new PixelPoint(screen.Bounds.X, screen.Bounds.Y);
        SystemDecorations = SystemDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        WindowState = WindowState.FullScreen;

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

                var scaling = _screen.Scaling;

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