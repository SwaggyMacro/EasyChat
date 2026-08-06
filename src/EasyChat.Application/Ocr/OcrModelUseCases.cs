using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Settings;

namespace EasyChat.Application.Ocr;

public sealed class OcrModelUseCases : IOcrModelUseCases
{
    private readonly IOcrModelStore _models;
    private readonly ISettingsUseCases _settings;

    public OcrModelUseCases(IOcrModelStore models, ISettingsUseCases settings)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public IReadOnlyList<OcrModelPackage> ModelPackages => _models.ModelPackages;

    public bool IsModelDownloaded(OcrModelPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return _models.IsModelDownloaded(package);
    }

    public Task DownloadModelAsync(
        OcrModelPackage package,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        var settings = _settings.Current;
        return _models.DownloadModelAsync(
            package,
            new OcrModelDownloadOptions(settings.Proxy.ProxyUrl, settings.Ocr.UseProxy),
            progress,
            cancellationToken);
    }

    public void DeleteModel(OcrModelPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        _models.DeleteModel(package);
    }
}
