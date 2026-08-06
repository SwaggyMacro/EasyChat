using System.Runtime.Versioning;
using EasyChat.Contracts.ApplicationData;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using Microsoft.Extensions.Logging;

namespace EasyChat.Infrastructure.Windows.Ocr;

[SupportedOSPlatform("windows")]
public sealed class WindowsOpenVinoOcr : IOcrRecognizer, IOcrModelStore, IDisposable
{
    private readonly IWindowsOcrBackend _backend;
    private readonly ILogger<WindowsOpenVinoOcr>? _logger;

    public WindowsOpenVinoOcr(
        IApplicationDataPaths applicationData,
        ILogger<WindowsOpenVinoOcr>? logger = null)
        : this(new OpenVinoWindowsOcrBackend(applicationData, logger), logger)
    {
    }

    internal WindowsOpenVinoOcr(
        IWindowsOcrBackend backend,
        ILogger<WindowsOpenVinoOcr>? logger = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _logger = logger;
    }

    public IReadOnlyList<OcrModelPackage> ModelPackages => OpenVinoOcrModelCatalog.Packages;

    public bool IsModelDownloaded(OcrModelPackage package) =>
        _backend.IsModelAvailable(OpenVinoOcrModelCatalog.ResolvePackage(package));

    public Task DownloadModelAsync(
        OcrModelPackage package,
        OcrModelDownloadOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return _backend.DownloadModelAsync(
            OpenVinoOcrModelCatalog.ResolvePackage(package),
            options,
            progress,
            cancellationToken);
    }

    public void DeleteModel(OcrModelPackage package) =>
        _backend.DeleteModel(OpenVinoOcrModelCatalog.ResolvePackage(package));

    public ValueTask<OcrRecognitionResult> RecognizeAsync(
        OcrRecognitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Image.PixelFormat != ImagePixelFormat.Bgra32)
            throw new NotSupportedException($"Pixel format '{request.Image.PixelFormat}' is not supported.");
        if (request.Language is null)
            throw new ArgumentException("The application must resolve the OCR language.", nameof(request));

        var language = OpenVinoOcrModelCatalog.ResolveLanguage(request.Language);
        var backendRegions = _backend.Recognize(
            request.Image,
            language,
            request.EnableRotation,
            request.Mode,
            request.IdleTimeoutSeconds,
            cancellationToken);

        var regions = backendRegions
            .Where(region => !string.IsNullOrWhiteSpace(region.Text))
            .Select(MapRegion)
            .ToArray();

        _logger?.LogDebug(
            "OCR ({Language}) recognized {RegionCount} regions.",
            language.Language.DisplayName,
            regions.Length);
        return ValueTask.FromResult(new OcrRecognitionResult(regions));
    }

    public void Dispose() => _backend.Dispose();

    private static OcrTextRegion MapRegion(WindowsOcrBackendRegion region)
    {
        var polygon = region.Polygon
            .Select(point => new ImagePoint(point.X, point.Y))
            .ToArray();
        return new OcrTextRegion(
            region.Text.Trim(),
            polygon,
            CalculateTextAngle(region.Polygon, region.FallbackAngle),
            region.Confidence);
    }

    internal static double CalculateTextAngle(
        IReadOnlyList<WindowsOcrPoint> polygon,
        double fallback = 0)
    {
        if (polygon.Count < 2)
            return NormalizeAngle(fallback);

        var longestLengthSquared = 0d;
        var angle = fallback;
        for (var index = 0; index < polygon.Count; index++)
        {
            var start = polygon[index];
            var end = polygon[(index + 1) % polygon.Count];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= longestLengthSquared)
                continue;

            longestLengthSquared = lengthSquared;
            angle = Math.Atan2(dy, dx) * 180d / Math.PI;
        }

        angle = NormalizeAngle(angle);
        return Math.Abs(angle) < 2 ? 0 : angle;
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle > 90) angle -= 180;
        while (angle <= -90) angle += 180;
        return angle;
    }
}

internal sealed record WindowsOcrPoint(double X, double Y);

internal sealed record WindowsOcrBackendRegion(
    string Text,
    IReadOnlyList<WindowsOcrPoint> Polygon,
    double FallbackAngle,
    double Confidence = 1d);

internal interface IWindowsOcrBackend : IDisposable
{
    bool IsModelAvailable(OpenVinoOcrModelPackageSpec package);

    Task DownloadModelAsync(
        OpenVinoOcrModelPackageSpec package,
        OcrModelDownloadOptions options,
        IProgress<double>? progress,
        CancellationToken cancellationToken);

    void DeleteModel(OpenVinoOcrModelPackageSpec package);

    IReadOnlyList<WindowsOcrBackendRegion> Recognize(
        ImageFrame image,
        WindowsOcrLanguageSelection language,
        bool enableRotation,
        OcrRecognitionMode mode,
        int idleTimeoutSeconds,
        CancellationToken cancellationToken);
}
