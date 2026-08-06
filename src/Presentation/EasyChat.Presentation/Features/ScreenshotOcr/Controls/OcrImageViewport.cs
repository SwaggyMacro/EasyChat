using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EasyChat.Contracts.Ocr;

namespace EasyChat.Presentation.Features.ScreenshotOcr.Controls;

public sealed class OcrImageViewport : Decorator
{
    private const double MinimumZoom = 0.1;
    private const double MaximumZoom = 8;
    private static readonly IBrush SurfaceBrush = new SolidColorBrush(Color.Parse("#17191C"));
    private static readonly IBrush BusySelectionBrush = new SolidColorBrush(Color.FromArgb(118, 13, 148, 136));
    private static readonly IBrush BusyDotBrush = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255));
    private static readonly IBrush BusyDotDimBrush = new SolidColorBrush(Color.FromArgb(95, 255, 255, 255));
    public static readonly StyledProperty<bool> IsSelectionBusyProperty =
        AvaloniaProperty.Register<OcrImageViewport, bool>(nameof(IsSelectionBusy));

    private Bitmap? _bitmap;
    private OcrRegionSpatialIndex _index = OcrRegionSpatialIndex.Empty;
    private readonly HashSet<int> _selected = [];
    private Point _pan;
    private Point _pointerStart;
    private Point _panStart;
    private Point? _selectionStart;
    private Point? _selectionCurrent;
    private int? _hovered;
    private IPointer? _capturedPointer;
    private double _zoom = 1;
    private bool _isPanning;
    private bool _spacePressed;
    private bool _toggleSelection;
    private int _clickCount;
    private readonly DispatcherTimer _busyTimer;
    private int _busyPhase;
    private TextBox? _textSelector;
    private int? _textSelectorRegion;

    public OcrImageViewport()
    {
        Focusable = true;
        ClipToBounds = true;
        _busyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _busyTimer.Tick += (_, _) =>
        {
            _busyPhase = (_busyPhase + 1) % 3;
            InvalidateVisual();
        };
    }

    public event Action<IReadOnlyList<int>>? SelectionChanged;
    public event Action<double>? ZoomChanged;

    public bool IsSelectionBusy
    {
        get => GetValue(IsSelectionBusyProperty);
        set => SetValue(IsSelectionBusyProperty, value);
    }

    public void SetBitmap(Bitmap bitmap, bool resetView = true)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        CloseTextSelector();
        _bitmap = bitmap;
        if (resetView)
            ResetView();
        else
            InvalidateVisual();
    }

    public void SetRegions(IReadOnlyList<OcrTextRegion> regions)
    {
        _index = new OcrRegionSpatialIndex(regions);
        CloseTextSelector();
        _selected.Clear();
        _hovered = null;
        SelectionChanged?.Invoke([]);
        InvalidateVisual();
    }

    public void ZoomIn() => SetZoomAt(_zoom * 1.2, Bounds.Center);
    public void ZoomOut() => SetZoomAt(_zoom / 1.2, Bounds.Center);

    public void ResetView()
    {
        CloseTextSelector();
        _zoom = 1;
        _pan = default;
        _selected.Clear();
        _hovered = null;
        ZoomChanged?.Invoke(100);
        SelectionChanged?.Invoke([]);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(SurfaceBrush, new Rect(Bounds.Size));
        if (_bitmap is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var transform = GetTransform();
        var destination = new Rect(
            transform.Origin.X,
            transform.Origin.Y,
            _bitmap.PixelSize.Width * transform.Scale,
            _bitmap.PixelSize.Height * transform.Scale);
        context.DrawImage(_bitmap, destination);

        var visibleImage = transform.ToImage(new Rect(Bounds.Size));
        foreach (var regionIndex in _index.Query(visibleImage))
        {
            var selected = _selected.Contains(regionIndex);
            var hovered = _hovered == regionIndex;
            if (!selected && !hovered)
                continue;

            var geometry = CreateGeometry(_index.Regions[regionIndex].Polygon, transform);
            var fill = selected && IsSelectionBusy
                ? BusySelectionBrush
                : selected
                ? new SolidColorBrush(Color.FromArgb(82, 22, 163, 155))
                : new SolidColorBrush(Color.FromArgb(62, 33, 150, 243));
            var pen = new Pen(
                selected ? Brushes.Teal : Brushes.DeepSkyBlue,
                selected ? 2 : 1.25);
            context.DrawGeometry(fill, pen, geometry);
            if (selected && IsSelectionBusy)
                DrawBusyIndicator(context, _index.GetBounds(regionIndex), transform);
        }

        if (_selectionStart is { } start && _selectionCurrent is { } current)
        {
            var selection = Normalize(start, current);
            context.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(40, 33, 150, 243)),
                new Pen(Brushes.DeepSkyBlue, 1),
                transform.ToView(selection));
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (IsFromTextSelector(e.Source))
            return;
        if (_bitmap is null || e.Delta.Y == 0)
            return;
        SetZoomAt(_zoom * Math.Pow(1.15, e.Delta.Y), e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsFromTextSelector(e.Source))
            return;
        if (_bitmap is null)
            return;
        CloseTextSelector();
        Focus();
        var point = e.GetCurrentPoint(this);
        var position = e.GetPosition(this);
        if (point.Properties.IsMiddleButtonPressed
            || (_spacePressed && point.Properties.IsLeftButtonPressed))
        {
            _isPanning = true;
            _pointerStart = position;
            _panStart = _pan;
            Capture(e.Pointer);
            Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Handled = true;
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            var imagePoint = GetTransform().ToImage(position);
            _selectionStart = imagePoint;
            _selectionCurrent = imagePoint;
            _toggleSelection = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            _clickCount = e.ClickCount;
            Capture(e.Pointer);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_bitmap is null)
            return;
        var position = e.GetPosition(this);
        if (_isPanning)
        {
            var delta = position - _pointerStart;
            _pan = new Point(_panStart.X + delta.X, _panStart.Y + delta.Y);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_selectionStart is not null)
        {
            _selectionCurrent = GetTransform().ToImage(position);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var imagePoint = GetTransform().ToImage(position);
        var hovered = _index.HitTest(imagePoint);
        if (_hovered != hovered)
        {
            _hovered = hovered;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isPanning)
        {
            _isPanning = false;
            Cursor = Cursor.Default;
            ReleaseCapture();
            e.Handled = true;
            return;
        }

        if (_selectionStart is not { } start)
            return;
        var end = GetTransform().ToImage(e.GetPosition(this));
        _selectionStart = null;
        _selectionCurrent = null;
        IReadOnlyList<int> candidates;
        int? clickedRegion = null;
        if (_clickCount > 1
            || Distance(start, end) <= 4 / Math.Max(GetTransform().Scale, 0.001))
        {
            var hit = _index.HitTest(end);
            clickedRegion = hit;
            candidates = hit is null ? [] : [hit.Value];
        }
        else
        {
            candidates = _index.Query(Normalize(start, end));
        }
        OcrRegionSelection.Apply(_selected, candidates, _toggleSelection);
        var openTextSelector = _clickCount > 1 && !_toggleSelection && clickedRegion is not null;
        _toggleSelection = false;
        _clickCount = 0;
        ReleaseCapture();
        SelectionChanged?.Invoke(_selected.Order().ToArray());
        InvalidateVisual();
        if (openTextSelector)
            OpenTextSelector(clickedRegion!.Value);
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_selectionStart is null && !_isPanning && _hovered is not null)
        {
            _hovered = null;
            InvalidateVisual();
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _capturedPointer = null;
        _isPanning = false;
        _selectionStart = null;
        _selectionCurrent = null;
        _toggleSelection = false;
        _clickCount = 0;
        Cursor = Cursor.Default;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Space)
        {
            _spacePressed = true;
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space)
        {
            _spacePressed = false;
            e.Handled = true;
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        InvalidateVisual();
        InvalidateArrange();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsSelectionBusyProperty)
            return;
        if (IsSelectionBusy)
        {
            CloseTextSelector();
            _busyTimer.Start();
        }
        else
        {
            _busyTimer.Stop();
            _busyPhase = 0;
        }
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (IsSelectionBusy)
            _busyTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _busyTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_textSelector is not null && _textSelectorRegion is { } regionIndex)
        {
            var region = GetTransform().ToView(_index.GetBounds(regionIndex));
            var layout = GetTextSelectorLayout(region, finalSize);
            _textSelector.FontSize = layout.FontSize;
            _textSelector.Arrange(layout.Bounds);
        }
        return finalSize;
    }

    internal static TextSelectorLayout GetTextSelectorLayout(Rect region, Size viewport)
    {
        const double margin = 8;
        var availableWidth = Math.Max(1, viewport.Width - margin * 2);
        var availableHeight = Math.Max(1, viewport.Height - margin * 2);
        var width = Math.Min(Math.Max(region.Width + 28, 220), availableWidth);
        var height = Math.Min(Math.Max(region.Height + 24, 76), Math.Min(220, availableHeight));
        var x = Math.Clamp(region.X - 8, margin, Math.Max(margin, viewport.Width - width - margin));
        var y = Math.Clamp(region.Y - 8, margin, Math.Max(margin, viewport.Height - height - margin));
        return new TextSelectorLayout(
            new Rect(x, y, width, height),
            Math.Clamp(region.Height * 0.62, 14, 26));
    }

    private void SetZoomAt(double value, Point anchor)
    {
        if (_bitmap is null)
            return;
        var oldTransform = GetTransform();
        var imageAnchor = oldTransform.ToImage(anchor);
        _zoom = Math.Clamp(value, MinimumZoom, MaximumZoom);
        var fitScale = GetFitScale();
        var newScale = fitScale * _zoom;
        var centeredOrigin = GetCenteredOrigin(newScale);
        _pan = new Point(
            anchor.X - imageAnchor.X * newScale - centeredOrigin.X,
            anchor.Y - imageAnchor.Y * newScale - centeredOrigin.Y);
        ZoomChanged?.Invoke(_zoom * 100);
        InvalidateVisual();
        InvalidateArrange();
    }

    private ViewportTransform GetTransform()
    {
        var scale = GetFitScale() * _zoom;
        var centered = GetCenteredOrigin(scale);
        return new ViewportTransform(
            scale,
            new Point(centered.X + _pan.X, centered.Y + _pan.Y));
    }

    private double GetFitScale()
    {
        if (_bitmap is null)
            return 1;
        return Math.Max(
            0.0001,
            Math.Min(
                Bounds.Width / Math.Max(1, _bitmap.PixelSize.Width),
                Bounds.Height / Math.Max(1, _bitmap.PixelSize.Height)));
    }

    private Point GetCenteredOrigin(double scale)
    {
        if (_bitmap is null)
            return default;
        return new Point(
            (Bounds.Width - _bitmap.PixelSize.Width * scale) / 2,
            (Bounds.Height - _bitmap.PixelSize.Height * scale) / 2);
    }

    private void Capture(IPointer pointer)
    {
        _capturedPointer = pointer;
        pointer.Capture(this);
    }

    private void ReleaseCapture()
    {
        var pointer = _capturedPointer;
        _capturedPointer = null;
        pointer?.Capture(null);
    }

    private void OpenTextSelector(int regionIndex)
    {
        CloseTextSelector();
        var editor = new TextBox
        {
            Text = _index.Regions[regionIndex].Text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Cursor = new Cursor(StandardCursorType.Ibeam),
            Background = new SolidColorBrush(Color.Parse("#F21D2024")),
            Foreground = Brushes.White,
            BorderBrush = Brushes.Teal,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(9, 7),
            SelectionBrush = Brushes.Teal
        };
        editor.SetValue(
            ScrollViewer.HorizontalScrollBarVisibilityProperty,
            Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        editor.SetValue(
            ScrollViewer.VerticalScrollBarVisibilityProperty,
            Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        _textSelector = editor;
        _textSelectorRegion = regionIndex;
        Child = editor;
        InvalidateMeasure();
        InvalidateArrange();
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_textSelector, editor))
                return;
            editor.Focus();
            editor.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void CloseTextSelector()
    {
        if (_textSelector is null)
            return;
        Child = null;
        _textSelector = null;
        _textSelectorRegion = null;
        InvalidateMeasure();
        InvalidateArrange();
    }

    private bool IsFromTextSelector(object? source) =>
        _textSelector is not null
        && source is Visual visual
        && (ReferenceEquals(visual, _textSelector)
            || visual.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, _textSelector)));

    private void DrawBusyIndicator(
        DrawingContext context,
        Rect imageBounds,
        ViewportTransform transform)
    {
        var viewBounds = transform.ToView(imageBounds);
        var radius = Math.Clamp(Math.Min(viewBounds.Width, viewBounds.Height) * 0.1, 2.5, 5);
        var spacing = radius * 2.7;
        for (var index = 0; index < 3; index++)
        {
            var x = viewBounds.Center.X + (index - 1) * spacing;
            context.DrawEllipse(
                index == _busyPhase ? BusyDotBrush : BusyDotDimBrush,
                null,
                new Point(x, viewBounds.Center.Y),
                radius,
                radius);
        }
    }

    private static StreamGeometry CreateGeometry(
        IReadOnlyList<ImagePoint> polygon,
        ViewportTransform transform)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(transform.ToView(polygon[0]), isFilled: true);
        for (var index = 1; index < polygon.Count; index++)
            context.LineTo(transform.ToView(polygon[index]));
        context.EndFigure(isClosed: true);
        return geometry;
    }

    private static Rect Normalize(Point first, Point second) => new(
        Math.Min(first.X, second.X),
        Math.Min(first.Y, second.Y),
        Math.Abs(first.X - second.X),
        Math.Abs(first.Y - second.Y));

    private static double Distance(Point first, Point second)
    {
        var x = first.X - second.X;
        var y = first.Y - second.Y;
        return Math.Sqrt(x * x + y * y);
    }

    internal readonly record struct ViewportTransform(double Scale, Point Origin)
    {
        public Point ToImage(Point point) => new(
            (point.X - Origin.X) / Scale,
            (point.Y - Origin.Y) / Scale);

        public Rect ToImage(Rect rect)
        {
            var topLeft = ToImage(rect.TopLeft);
            var bottomRight = ToImage(rect.BottomRight);
            return new Rect(topLeft, bottomRight);
        }

        public Point ToView(ImagePoint point) => new(
            Origin.X + point.X * Scale,
            Origin.Y + point.Y * Scale);

        public Rect ToView(Rect rect) => new(
            Origin.X + rect.X * Scale,
            Origin.Y + rect.Y * Scale,
            rect.Width * Scale,
            rect.Height * Scale);
    }

    internal readonly record struct TextSelectorLayout(Rect Bounds, double FontSize);
}

internal static class OcrRegionSelection
{
    internal static void Apply(
        HashSet<int> selected,
        IReadOnlyList<int> candidates,
        bool toggle)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(candidates);
        if (!toggle)
            selected.Clear();

        foreach (var candidate in candidates)
        {
            if (!toggle || !selected.Remove(candidate))
                selected.Add(candidate);
        }
    }
}

internal sealed class OcrRegionSpatialIndex
{
    private const int CellSize = 256;
    private readonly Dictionary<long, List<int>> _cells = [];
    private readonly Rect[] _bounds;

    internal static OcrRegionSpatialIndex Empty { get; } = new([]);

    internal OcrRegionSpatialIndex(IReadOnlyList<OcrTextRegion> regions)
    {
        Regions = regions;
        _bounds = new Rect[regions.Count];
        for (var index = 0; index < regions.Count; index++)
        {
            var bounds = GetBounds(regions[index].Polygon);
            _bounds[index] = bounds;
            foreach (var cell in GetCells(bounds))
            {
                if (!_cells.TryGetValue(cell, out var values))
                {
                    values = [];
                    _cells.Add(cell, values);
                }
                values.Add(index);
            }
        }
    }

    internal IReadOnlyList<OcrTextRegion> Regions { get; }

    internal Rect GetBounds(int index) => _bounds[index];

    internal int? HitTest(Point point)
    {
        if (!_cells.TryGetValue(CellKey(FloorCell(point.X), FloorCell(point.Y)), out var candidates))
            return null;
        for (var candidate = candidates.Count - 1; candidate >= 0; candidate--)
        {
            var index = candidates[candidate];
            if (_bounds[index].Contains(point) && Contains(Regions[index].Polygon, point))
                return index;
        }
        return null;
    }

    internal IReadOnlyList<int> Query(Rect area)
    {
        var found = new HashSet<int>();
        foreach (var cell in GetCells(area))
        {
            if (!_cells.TryGetValue(cell, out var candidates))
                continue;
            foreach (var index in candidates)
            {
                if (_bounds[index].Intersects(area))
                    found.Add(index);
            }
        }
        return found.ToArray();
    }

    private static Rect GetBounds(IReadOnlyList<ImagePoint> polygon)
    {
        var left = polygon.Min(point => point.X);
        var top = polygon.Min(point => point.Y);
        var right = polygon.Max(point => point.X);
        var bottom = polygon.Max(point => point.Y);
        return new Rect(left, top, Math.Max(0.01, right - left), Math.Max(0.01, bottom - top));
    }

    private static bool Contains(IReadOnlyList<ImagePoint> polygon, Point point)
    {
        var inside = false;
        for (int current = 0, previous = polygon.Count - 1;
             current < polygon.Count;
             previous = current++)
        {
            var a = polygon[current];
            var b = polygon[previous];
            if ((a.Y > point.Y) != (b.Y > point.Y)
                && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private static IEnumerable<long> GetCells(Rect area)
    {
        var left = FloorCell(area.Left);
        var top = FloorCell(area.Top);
        var right = FloorCell(area.Right);
        var bottom = FloorCell(area.Bottom);
        for (var y = top; y <= bottom; y++)
        for (var x = left; x <= right; x++)
            yield return CellKey(x, y);
    }

    private static int FloorCell(double value) => (int)Math.Floor(value / CellSize);
    private static long CellKey(int x, int y) => ((long)x << 32) ^ (uint)y;
}
