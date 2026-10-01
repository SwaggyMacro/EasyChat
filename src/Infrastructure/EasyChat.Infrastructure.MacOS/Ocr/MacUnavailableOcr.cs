using EasyChat.Contracts.Ocr;

namespace EasyChat.Infrastructure.MacOS.Ocr;

/// <summary>
/// Stands in for OCR while phase 10 is suspended: the PaddleOCR stack has no usable macOS arm64
/// inference plugin or OpenCvSharp native library (see macOS.md). It offers no model packages and
/// fails every recognition explicitly, so the app composes and starts without pretending OCR works.
/// </summary>
public sealed class MacUnavailableOcr : IOcrRecognizer, IOcrModelStore
{
    internal const string Reason = "OCR is not available on macOS yet.";

    public IReadOnlyList<OcrModelPackage> ModelPackages { get; } = [];

    public ValueTask<OcrRecognitionResult> RecognizeAsync(
        OcrRecognitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        throw new PlatformNotSupportedException(Reason);
    }

    public bool IsModelDownloaded(OcrModelPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return false;
    }

    public Task DownloadModelAsync(
        OcrModelPackage package,
        OcrModelDownloadOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(options);
        return Task.FromException(new PlatformNotSupportedException(Reason));
    }

    public void DeleteModel(OcrModelPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
    }
}
