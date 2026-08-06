using System.Runtime.Versioning;
using EasyChat.Contracts.ImageTranslation;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.Windows.ImageTranslation;

[SupportedOSPlatform("windows")]
public sealed class WindowsImageBackgroundCleaner : IImageBackgroundCleaner
{
    public ImageFrame RemoveText(
        ImageFrame source,
        IReadOnlyList<OcrTextRegion> regions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(regions);
        cancellationToken.ThrowIfCancellationRequested();

        if (source.PixelFormat != ImagePixelFormat.Bgra32)
            throw new NotSupportedException($"Pixel format '{source.PixelFormat}' is not supported.");
        if (regions.Count == 0)
            return source;
        return WindowsImageBackgroundCleanerWorkerClient.RemoveText(
            source,
            regions,
            cancellationToken);
    }
}
