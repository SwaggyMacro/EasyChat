using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using EasyChat.Contracts.ImageTranslation;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;

namespace EasyChat.Presentation.ImageTranslation;

public sealed class AvaloniaImageTranslationRenderer : IImageTranslationRenderer
{
    private const double MinimumFontSize = 1;
    private readonly IImageBackgroundCleaner _backgroundCleaner;
    private readonly SemaphoreSlim _renderGate = new(1, 1);

    public AvaloniaImageTranslationRenderer(IImageBackgroundCleaner backgroundCleaner)
    {
        _backgroundCleaner = backgroundCleaner
                             ?? throw new ArgumentNullException(nameof(backgroundCleaner));
    }

    public async Task<ImageTranslationRenderResult> RenderAsync(
        ImageFrame source,
        IReadOnlyList<ImageTranslationOverlay> overlays,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(overlays);
        cancellationToken.ThrowIfCancellationRequested();

        await _renderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var warnings = new List<string>();
            var renderable = overlays
                .Where(overlay => CanFitText(overlay, source))
                .ToArray();
            foreach (var overlay in overlays.Except(renderable))
                warnings.Add($"Translation did not fit: {overlay.Region.Text.Trim()}");

            if (renderable.Length == 0)
                return new ImageTranslationRenderResult(source, warnings, 0);

            var backgroundFrame = _backgroundCleaner.RemoveText(
                source,
                renderable.Select(overlay => overlay.Region).ToArray(),
                cancellationToken);
            using var background = AvaloniaImageFrames.ToBitmap(backgroundFrame);
            using var output = new RenderTargetBitmap(background.PixelSize, background.Dpi);
            var pixelToDip = PixelToDipScale(background.PixelSize, background.Size);

            using (var context = output.CreateDrawingContext())
            {
                context.DrawImage(background, new Rect(background.Size));
                foreach (var overlay in renderable)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var geometry = GetGeometry(overlay.Region);
                    var boxWidth = geometry.BoxWidth * pixelToDip.X;
                    var boxHeight = geometry.BoxHeight * pixelToDip.Y;
                    var originalBounds = new Rect(0, 0, boxWidth, boxHeight);
                    var brightness = SampleBrightness(backgroundFrame, geometry.Bounds);
                    var brush = brightness < 135 ? Brushes.White : Brushes.Black;
                    var preferredFontSize = CalculatePreferredFontSize(originalBounds, overlay.Region.Angle);
                    var layout = CreateLayout(
                        overlay.Translation,
                        boxWidth,
                        boxHeight,
                        preferredFontSize,
                        brush);
                    if (layout is null)
                    {
                        warnings.Add($"Translation did not fit: {overlay.Region.Text.Trim()}");
                        continue;
                    }

                    var center = new Point(
                        geometry.Center.X * pixelToDip.X,
                        geometry.Center.Y * pixelToDip.Y);
                    var matrix = Matrix.CreateRotation(overlay.Region.Angle * Math.PI / 180d)
                                 * Matrix.CreateTranslation(center.X, center.Y);
                    using (context.PushTransform(matrix))
                    {
                        var y = -layout.Height / 2;
                        foreach (var line in layout.Lines)
                        {
                            context.DrawText(line, new Point(-boxWidth / 2, y));
                            y += line.Height;
                        }
                    }
                }
            }

            var result = AvaloniaImageFrames.ToImageFrame(output);
            return new ImageTranslationRenderResult(result, warnings, renderable.Length);
        }
        finally
        {
            _renderGate.Release();
        }
    }

    public static Vector PixelToDipScale(PixelSize pixelSize, Size dipSize) =>
        new(
            dipSize.Width / Math.Max(1, pixelSize.Width),
            dipSize.Height / Math.Max(1, pixelSize.Height));

    public static double CalculatePreferredFontSize(Rect originalBounds, double angle)
    {
        var normalizedAngle = Math.Abs(NormalizeAngle(angle));
        var textHeight = normalizedAngle > 45 ? originalBounds.Width : originalBounds.Height;
        return Math.Max(MinimumFontSize, textHeight * 0.72);
    }

    public static bool IsLayoutWithinBox(
        double layoutWidth,
        double layoutHeight,
        double boxWidth,
        double boxHeight) =>
        layoutWidth <= boxWidth && layoutHeight <= boxHeight;

    private static bool CanFitText(ImageTranslationOverlay overlay, ImageFrame image)
    {
        var geometry = GetGeometry(overlay.Region);
        var scaleX = 96d / Math.Max(1d, image.DpiX);
        var scaleY = 96d / Math.Max(1d, image.DpiY);
        var boxWidth = geometry.BoxWidth * scaleX;
        var boxHeight = geometry.BoxHeight * scaleY;
        var preferredFontSize = CalculatePreferredFontSize(
            new Rect(0, 0, boxWidth, boxHeight),
            overlay.Region.Angle);
        return CreateLayout(
                   overlay.Translation,
                   boxWidth,
                   boxHeight,
                   preferredFontSize,
                   Brushes.Black)
               is not null;
    }

    private static TextLayout? CreateLayout(
        string text,
        double width,
        double height,
        double preferredFontSize,
        IBrush brush)
    {
        if (width <= 1 || height <= 1)
            return null;

        var fontSize = Math.Max(MinimumFontSize, preferredFontSize);
        while (fontSize >= MinimumFontSize)
        {
            var lines = WrapText(text, width, fontSize, brush);
            var totalHeight = lines.Sum(line => line.Height);
            var totalWidth = lines.Count > 0 ? lines.Max(line => line.Width) : 0;
            if (lines.Count > 0 && IsLayoutWithinBox(totalWidth, totalHeight, width, height))
                return new TextLayout(lines, totalWidth, totalHeight);

            fontSize -= Math.Max(1, fontSize * 0.08);
        }

        return null;
    }

    private static IReadOnlyList<FormattedText> WrapText(
        string text,
        double maxWidth,
        double fontSize,
        IBrush brush)
    {
        var lines = new List<FormattedText>();
        foreach (var sourceLine in text.Replace("\r", string.Empty).Split('\n'))
        {
            var current = new StringBuilder();
            foreach (var character in sourceLine)
            {
                var candidate = current.ToString() + character;
                var measured = Measure(candidate, fontSize, brush);
                if (current.Length > 0 && measured.Width > maxWidth)
                {
                    lines.Add(Measure(current.ToString(), fontSize, brush));
                    current.Clear();
                }

                current.Append(character);
            }

            if (current.Length > 0)
                lines.Add(Measure(current.ToString(), fontSize, brush));
        }

        return lines;
    }

    private static FormattedText Measure(
        string text,
        double fontSize,
        IBrush brush) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"),
            fontSize,
            brush);

    private static double SampleBrightness(ImageFrame frame, Rect bounds)
    {
        var left = Math.Max(0, (int)bounds.Left);
        var top = Math.Max(0, (int)bounds.Top);
        var width = Math.Max(1, Math.Min(frame.Width - left, (int)bounds.Width));
        var height = Math.Max(1, Math.Min(frame.Height - top, (int)bounds.Height));
        var pixels = frame.Pixels.Span;
        double red = 0;
        double green = 0;
        double blue = 0;
        var count = 0;
        for (var y = top; y < top + height; y++)
        {
            var row = y * frame.Stride;
            for (var x = left; x < left + width; x++)
            {
                var offset = row + x * 4;
                blue += pixels[offset];
                green += pixels[offset + 1];
                red += pixels[offset + 2];
                count++;
            }
        }

        return count == 0
            ? 128
            : 0.299 * red / count + 0.587 * green / count + 0.114 * blue / count;
    }

    private static RegionGeometry GetGeometry(OcrTextRegion region)
    {
        var left = region.Polygon.Min(point => point.X);
        var top = region.Polygon.Min(point => point.Y);
        var right = region.Polygon.Max(point => point.X);
        var bottom = region.Polygon.Max(point => point.Y);
        var center = new Point(
            region.Polygon.Average(point => point.X),
            region.Polygon.Average(point => point.Y));
        var edges = new List<double>(region.Polygon.Count);
        for (var index = 0; index < region.Polygon.Count; index++)
        {
            var start = region.Polygon[index];
            var end = region.Polygon[(index + 1) % region.Polygon.Count];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length > 0.01)
                edges.Add(length);
        }

        var bounds = new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        return edges.Count == 0
            ? new RegionGeometry(bounds, center, bounds.Width, bounds.Height)
            : new RegionGeometry(bounds, center, edges.Max(), edges.Min());
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }

    private sealed record RegionGeometry(
        Rect Bounds,
        Point Center,
        double BoxWidth,
        double BoxHeight);

    private sealed record TextLayout(
        IReadOnlyList<FormattedText> Lines,
        double Width,
        double Height);
}
