using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EasyChat.Contracts.Capture;
using EasyChat.Contracts.Platform;
using Key = Avalonia.Input.Key;

namespace EasyChat.Presentation.Features.Capture.Views;

public partial class OverlayWindowView : Window
{
    private readonly ScreenDescriptor? _screen;
    private readonly bool _regionOnly;
    private readonly CaptureOverlayAction _defaultAction = CaptureOverlayAction.Translation;
    private readonly Image? _capturedScreenImage;
    private readonly Rectangle? _selectionRectangle;
    private readonly Border? _hintBorder;
    private readonly TextBlock? _hintTextBlock;
    private readonly Control? _toolbarBorder;
    private readonly Control? _copyMenuBorder;
    private readonly Control? _copyButton;
    private readonly Border[] _handles = new Border[8];
    private DispatcherTimer? _menuCloseTimer;
    private IPointer? _capturedPointer;
    private PhysicalScreenRegion? _selection;
    private CaptureSelectionMode _mode;
    private bool _hintHost;
    private bool _sessionClosing;
    private bool _releasingPointerCapture;
    private bool _opened;
    private bool _closed;

    public OverlayWindowView() => InitializeComponent();

    internal OverlayWindowView(
        ScreenDescriptor screen,
        IImage capturedImage,
        bool regionOnly,
        CaptureOverlayAction defaultAction = CaptureOverlayAction.Translation,
        CaptureToolbarMode toolbarMode = CaptureToolbarMode.Full)
    {
        InitializeComponent();
        _screen = screen;
        _regionOnly = regionOnly;
        _defaultAction = defaultAction;
        ShowInTaskbar = false;
        WindowState = WindowState.Normal;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowDecorations = WindowDecorations.None;
        ExtendClientAreaToDecorationsHint = true;
        CanResize = false;
        Topmost = true;
        ShowActivated = false;
        Position = new PixelPoint(screen.Bounds.X, screen.Bounds.Y);
        var logicalSize = CaptureOverlayGeometry.GetLogicalSize(screen);
        Width = logicalSize.Width;
        Height = logicalSize.Height;
        Background = Brushes.Black;

        _capturedScreenImage = Require<Image>("CapturedScreenImage");
        _capturedScreenImage.Source = capturedImage;
        _selectionRectangle = Require<Rectangle>("SelectionRectangle");
        _hintBorder = Require<Border>("HintBorder");
        _hintTextBlock = Require<TextBlock>("HintTextBlock");
        _toolbarBorder = Require<Control>("ToolbarBorder");
        _copyMenuBorder = Require<Control>("CopyMenuBorder");
        _copyButton = Require<Control>("CopyButton");
        _copyButton.IsVisible = toolbarMode == CaptureToolbarMode.Full;
        Require<Control>("OcrButton").IsVisible = toolbarMode == CaptureToolbarMode.Full;
        _handles =
        [
            Require<Border>("HandleTopLeft"),
            Require<Border>("HandleTopCenter"),
            Require<Border>("HandleTopRight"),
            Require<Border>("HandleRightCenter"),
            Require<Border>("HandleBottomRight"),
            Require<Border>("HandleBottomCenter"),
            Require<Border>("HandleBottomLeft"),
            Require<Border>("HandleLeftCenter")
        ];
        _hintTextBlock.Text = regionOnly ? Lang.Resources.FixedArea_Hint : Lang.Resources.Screenshot_Hint;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
    }

    internal ScreenDescriptor Screen => _screen
        ?? throw new InvalidOperationException("The design-time overlay has no screen descriptor.");

    internal event Action<OverlayWindowView, PhysicalScreenPoint, CaptureResizeHandle, bool>? InteractionStarted;
    internal event Action<OverlayWindowView, PhysicalScreenPoint>? InteractionMoved;
    internal event Action<OverlayWindowView, PhysicalScreenPoint>? InteractionEnded;
    internal event Action<OverlayWindowView, CaptureOverlayAction>? ActionRequested;
    internal event Action? ResetRequested;
    internal event Action? CancelRequested;
    internal event Action? ClosedUnexpectedly;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_screen is null)
            return;

        _opened = true;
        Position = new PixelPoint(_screen.Bounds.X, _screen.Bounds.Y);
        var scaling = PositiveScale(RenderScaling);
        ClientSize = new Size(
            _screen.Bounds.Width / scaling,
            _screen.Bounds.Height / scaling);
        RenderSelection(_selection, _mode, showToolbar: false);
    }

    internal void SetHintHost(bool value)
    {
        _hintHost = value;
        if (_hintBorder is not null)
            _hintBorder.IsVisible = value && _mode == CaptureSelectionMode.Idle;
    }

    internal void RenderSelection(
        PhysicalScreenRegion? selection,
        CaptureSelectionMode mode,
        bool showToolbar)
    {
        _selection = selection;
        _mode = mode;
        if (_selectionRectangle is null || _toolbarBorder is null || _hintBorder is null)
            return;

        _toolbarBorder.IsVisible = false;
        if (_copyMenuBorder is not null)
            _copyMenuBorder.IsVisible = false;
        HideHandles();
        _hintBorder.IsVisible = _hintHost && mode == CaptureSelectionMode.Idle;

        if (_screen is null || selection is not { IsEmpty: false } region ||
            Intersect(region, _screen.Bounds) is not { } visible)
        {
            _selectionRectangle.IsVisible = false;
            Cursor = Cursor.Default;
            return;
        }

        _selectionRectangle.IsVisible = true;
        var local = ToClientRect(visible);
        SetSelection(local);

        if (mode == CaptureSelectionMode.Done)
        {
            ShowHandles(region);
            if (showToolbar)
                UpdateToolbarPosition(local);
            Cursor = Cursor.Default;
        }
        else if (mode == CaptureSelectionMode.Moving)
        {
            Cursor = new Cursor(StandardCursorType.SizeAll);
        }
        else
        {
            Cursor = Cursor.Default;
        }
    }

    internal void PrepareForSessionClose()
    {
        _sessionClosing = true;
        _menuCloseTimer?.Stop();
        ReleasePointerCapture();
        if (_capturedScreenImage is not null)
            _capturedScreenImage.Source = null;
    }

    internal void CloseSessionWindow()
    {
        PrepareForSessionClose();
        if (_opened && !_closed)
            Close();
    }

    private static double PositiveScale(double value) => value > 0 ? value : 1d;

    private T Require<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new InvalidOperationException($"{name} not found.");

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_screen is null)
            return;

        var current = e.GetCurrentPoint(this);
        if (current.Properties.IsRightButtonPressed)
        {
            CancelRequested?.Invoke();
            return;
        }
        if (!current.Properties.IsLeftButtonPressed ||
            _toolbarBorder?.IsPointerOver == true ||
            _copyMenuBorder?.IsPointerOver == true)
        {
            return;
        }

        var logical = e.GetPosition(this);
        var physical = ToPhysicalPoint(logical);
        var handle = GetHitHandle(logical);
        var insideSelection = _selection is { } selection && Contains(selection, physical);
        _capturedPointer = e.Pointer;
        e.Pointer.Capture(this);
        InteractionStarted?.Invoke(this, physical, handle, insideSelection);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_screen is null)
            return;

        var logical = e.GetPosition(this);
        var physical = ToPhysicalPoint(logical);
        if (_mode == CaptureSelectionMode.Idle)
            MoveHint(logical);
        else if (_mode == CaptureSelectionMode.Done)
            UpdateCursor(logical, physical);
        InteractionMoved?.Invoke(this, physical);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_screen is null)
            return;
        if (_mode is not (
                CaptureSelectionMode.Selecting or
                CaptureSelectionMode.Resizing or
                CaptureSelectionMode.Moving))
        {
            ReleasePointerCapture();
            return;
        }

        var physical = ToPhysicalPoint(e.GetPosition(this));
        try
        {
            InteractionEnded?.Invoke(this, physical);
        }
        finally
        {
            ReleasePointerCapture();
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _capturedPointer = null;
        if (_releasingPointerCapture || _sessionClosing)
            return;
        if (_mode is CaptureSelectionMode.Selecting or
            CaptureSelectionMode.Resizing or
            CaptureSelectionMode.Moving)
        {
            CancelRequested?.Invoke();
        }
    }

    private void ReleasePointerCapture()
    {
        var pointer = _capturedPointer;
        if (pointer is null)
            return;
        _releasingPointerCapture = true;
        try
        {
            pointer.Capture(null);
        }
        finally
        {
            _capturedPointer = null;
            _releasingPointerCapture = false;
        }
    }

    public void ConfirmButton_OnClick(object? sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, _defaultAction);
    public void OcrButton_OnClick(object? sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, CaptureOverlayAction.OcrWorkbench);
    public void CopyOriginal_OnClick(object? sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, CaptureOverlayAction.CopyOriginal);
    public void CopyTranslated_OnClick(object? sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, CaptureOverlayAction.CopyTranslated);
    public void CopyBilingual_OnClick(object? sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, CaptureOverlayAction.CopyBilingual);
    public void CopyImageTranslated_OnClick(object? sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, CaptureOverlayAction.CopyImageTranslated);
    public void ResetButton_OnClick(object? sender, RoutedEventArgs e) => ResetRequested?.Invoke();
    public void CancelButton_OnClick(object? sender, RoutedEventArgs e) => CancelRequested?.Invoke();

    private Rect ToClientRect(PhysicalScreenRegion region)
    {
        var topLeft = this.PointToClient(new PixelPoint(region.X, region.Y));
        var bottomRight = this.PointToClient(new PixelPoint(
            checked(region.X + region.Width),
            checked(region.Y + region.Height)));
        return new Rect(
            Math.Min(topLeft.X, bottomRight.X),
            Math.Min(topLeft.Y, bottomRight.Y),
            Math.Abs(bottomRight.X - topLeft.X),
            Math.Abs(bottomRight.Y - topLeft.Y));
    }

    private PhysicalScreenPoint ToPhysicalPoint(Point logical)
    {
        var point = this.PointToScreen(logical);
        return new PhysicalScreenPoint(point.X, point.Y);
    }

    private void SetSelection(Rect selection)
    {
        Canvas.SetLeft(_selectionRectangle!, selection.X);
        Canvas.SetTop(_selectionRectangle!, selection.Y);
        _selectionRectangle!.Width = selection.Width;
        _selectionRectangle.Height = selection.Height;
    }

    private void ShowHandles(PhysicalScreenRegion selection)
    {
        if (_screen is null)
            return;

        var left = selection.X;
        var top = selection.Y;
        var right = checked(selection.X + selection.Width);
        var bottom = checked(selection.Y + selection.Height);
        var centerX = left + selection.Width / 2;
        var centerY = top + selection.Height / 2;
        PhysicalScreenPoint[] points =
        [
            new(left, top),
            new(centerX, top),
            new(right, top),
            new(right, centerY),
            new(right, bottom),
            new(centerX, bottom),
            new(left, bottom),
            new(left, centerY)
        ];

        for (var index = 0; index < _handles.Length; index++)
        {
            if (!ContainsInclusive(_screen.Bounds, points[index]))
                continue;
            var local = this.PointToClient(new PixelPoint(points[index].X, points[index].Y));
            Canvas.SetLeft(_handles[index], local.X - _handles[index].Width / 2);
            Canvas.SetTop(_handles[index], local.Y - _handles[index].Height / 2);
            _handles[index].IsVisible = true;
        }
    }

    private void HideHandles()
    {
        foreach (var handle in _handles)
            handle.IsVisible = false;
    }

    private CaptureResizeHandle GetHitHandle(Point point)
    {
        for (var index = 0; index < _handles.Length; index++)
        {
            var handle = _handles[index];
            if (!handle.IsVisible)
                continue;
            var hit = new Rect(Canvas.GetLeft(handle), Canvas.GetTop(handle), handle.Width, handle.Height)
                .Inflate(5);
            if (hit.Contains(point))
                return (CaptureResizeHandle)(index + 1);
        }
        return CaptureResizeHandle.None;
    }

    private void UpdateCursor(Point logical, PhysicalScreenPoint physical)
    {
        Cursor = GetHitHandle(logical) switch
        {
            CaptureResizeHandle.TopLeft => new Cursor(StandardCursorType.TopLeftCorner),
            CaptureResizeHandle.TopCenter or CaptureResizeHandle.BottomCenter =>
                new Cursor(StandardCursorType.SizeNorthSouth),
            CaptureResizeHandle.TopRight => new Cursor(StandardCursorType.TopRightCorner),
            CaptureResizeHandle.RightCenter or CaptureResizeHandle.LeftCenter =>
                new Cursor(StandardCursorType.SizeWestEast),
            CaptureResizeHandle.BottomRight => new Cursor(StandardCursorType.BottomRightCorner),
            CaptureResizeHandle.BottomLeft => new Cursor(StandardCursorType.BottomLeftCorner),
            _ when _selection is { } selection && Contains(selection, physical) =>
                new Cursor(StandardCursorType.SizeAll),
            _ => Cursor.Default
        };
    }

    private void MoveHint(Point pointer)
    {
        if (_hintBorder is null || !_hintHost)
            return;
        var target = new Point(30, 30);
        Canvas.SetLeft(_hintBorder, target.X);
        Canvas.SetTop(_hintBorder, target.Y);
        _hintBorder.IsVisible = !new Rect(target, _hintBorder.Bounds.Size).Contains(pointer);
    }

    private void UpdateToolbarPosition(Rect visibleSelection)
    {
        if (_toolbarBorder is null || _copyButton is null)
            return;
        _toolbarBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var toolbarSize = _toolbarBorder.DesiredSize;
        var width = toolbarSize.Width > 0 ? toolbarSize.Width : 200;
        var height = toolbarSize.Height > 0 ? toolbarSize.Height : 60;
        _copyButton.IsVisible = !_regionOnly;
        var x = Math.Clamp(
            visibleSelection.Right - width,
            10,
            Math.Max(10, Bounds.Width - width - 10));
        var y = visibleSelection.Bottom + 10;
        if (y + height > Bounds.Height)
            y = Math.Max(0, visibleSelection.Y - height - 10);
        Canvas.SetLeft(_toolbarBorder, x);
        Canvas.SetTop(_toolbarBorder, y);
        _toolbarBorder.IsVisible = true;
    }

    private void CopyButton_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        _menuCloseTimer?.Stop();
        if (_toolbarBorder?.IsVisible != true || _copyButton is null || _copyMenuBorder is null)
            return;
        var canvas = this.FindControl<Canvas>("MainCanvas");
        var position = canvas is null ? null : _copyButton.TranslatePoint(default, canvas);
        if (position is null)
            return;
        Canvas.SetLeft(_copyMenuBorder, position.Value.X);
        var top = Canvas.GetTop(_toolbarBorder) + _toolbarBorder.Bounds.Height + 2;
        if (top + 150 > Height)
            top = Canvas.GetTop(_toolbarBorder) - 152;
        Canvas.SetTop(_copyMenuBorder, top);
        _copyMenuBorder.IsVisible = true;
    }

    private void CopyButton_OnPointerExited(object? sender, PointerEventArgs e) => StartMenuCloseTimer();
    private void CopyMenu_OnPointerEntered(object? sender, PointerEventArgs e) => _menuCloseTimer?.Stop();
    private void CopyMenu_OnPointerExited(object? sender, PointerEventArgs e) => StartMenuCloseTimer();

    private void StartMenuCloseTimer()
    {
        _menuCloseTimer?.Stop();
        _menuCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _menuCloseTimer.Tick += (_, _) =>
        {
            if (_copyMenuBorder is not null)
                _copyMenuBorder.IsVisible = false;
            _menuCloseTimer.Stop();
        };
        _menuCloseTimer.Start();
    }

    private void InputElement_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            CancelRequested?.Invoke();
        else if (e.Key == Key.Enter && _toolbarBorder?.IsVisible == true)
            ActionRequested?.Invoke(this, _defaultAction);
    }

    private void TopLevel_OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _menuCloseTimer?.Stop();
        if (!_sessionClosing)
            ClosedUnexpectedly?.Invoke();
    }

    private static bool Contains(PhysicalScreenRegion region, PhysicalScreenPoint point) =>
        point.X >= region.X && point.X < checked(region.X + region.Width) &&
        point.Y >= region.Y && point.Y < checked(region.Y + region.Height);

    private static bool ContainsInclusive(PhysicalScreenRegion region, PhysicalScreenPoint point) =>
        point.X >= region.X && point.X <= checked(region.X + region.Width) &&
        point.Y >= region.Y && point.Y <= checked(region.Y + region.Height);

    private static PhysicalScreenRegion? Intersect(
        PhysicalScreenRegion first,
        PhysicalScreenRegion second)
    {
        var left = Math.Max(first.X, second.X);
        var top = Math.Max(first.Y, second.Y);
        var right = Math.Min(
            checked(first.X + first.Width),
            checked(second.X + second.Width));
        var bottom = Math.Min(
            checked(first.Y + first.Height),
            checked(second.Y + second.Height));
        return right > left && bottom > top
            ? new PhysicalScreenRegion(left, top, right - left, bottom - top)
            : null;
    }
}
