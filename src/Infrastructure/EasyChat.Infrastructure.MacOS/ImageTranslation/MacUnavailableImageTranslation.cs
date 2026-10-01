using EasyChat.Contracts.ImageTranslation;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;

namespace EasyChat.Infrastructure.MacOS.ImageTranslation;

/// <summary>
/// Stands in for image text removal while phase 11 is suspended on the same missing OpenCvSharp
/// native library as OCR. It offers no model packages and fails every removal explicitly rather than
/// returning the untouched image as if the text had been erased.
/// </summary>
public sealed class MacUnavailableImageTranslation : IImageBackgroundCleaner, IImageTranslationModelStore
{
    private const string Reason = "Image text removal is not available on macOS yet.";

    public IReadOnlyList<ImageTranslationModelPackage> ModelPackages { get; } = [];

    public Task<ImageFrame> RemoveTextAsync(
        ImageFrame source,
        IReadOnlyList<OcrTextRegion> regions,
        ImageTextEraseMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(regions);
        return Task.FromException<ImageFrame>(new PlatformNotSupportedException(Reason));
    }

    public bool IsModelDownloaded(ImageTranslationModelPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return false;
    }

    public Task DownloadModelAsync(
        ImageTranslationModelPackage package,
        NetworkProxyMode proxyMode,
        string? proxyUrl,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        return Task.FromException(new PlatformNotSupportedException(Reason));
    }

    public void DeleteModel(ImageTranslationModelPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
    }
}
