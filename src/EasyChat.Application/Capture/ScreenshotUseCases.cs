using System.Runtime.CompilerServices;
using EasyChat.Contracts.Capture;
using EasyChat.Contracts.ImageTranslation;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Translation;

namespace EasyChat.Application.Capture;

public sealed class ScreenshotUseCases(
    ISettingsUseCases settings,
    ITranslationLanguageCatalog languages,
    IOcrRecognitionUseCases ocr,
    ITranslationUseCases translation,
    IImageTranslationUseCases imageTranslation) : IScreenshotUseCases
{
    private readonly ISettingsUseCases _settings = settings;
    private readonly ITranslationLanguageCatalog _languages = languages;
    private readonly IOcrRecognitionUseCases _ocr = ocr;
    private readonly ITranslationUseCases _translation = translation;
    private readonly IImageTranslationUseCases _imageTranslation = imageTranslation;

    public OcrLanguage ResolveOcrLanguage(OcrLanguage? requestedLanguage = null)
    {
        if (requestedLanguage is not null
            && !string.Equals(requestedLanguage.Id, OcrLanguages.Auto.Id, StringComparison.OrdinalIgnoreCase)
            && OcrLanguages.TryGet(requestedLanguage.Id, out var requested))
        {
            return requested;
        }

        var global = ResolveOcrLanguage(_settings.Current.General.SourceLanguage.Id);
        return global is not null
               && !string.Equals(global.Id, OcrLanguages.Auto.Id, StringComparison.OrdinalIgnoreCase)
            ? global
            : OcrLanguages.ChineseSimplified;
    }

    public ValueTask<OcrRecognitionResult> RecognizeAsync(
        ImageFrame image,
        bool enableRotation,
        OcrLanguage? language = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var settings = _settings.Current;
        return _ocr.RecognizeAsync(
            new OcrRecognitionRequest(
                image,
                ResolveOcrLanguage(language),
                enableRotation,
                settings.Screenshot.OcrMode,
                settings.Screenshot.OcrIdleTimeoutSeconds),
            cancellationToken);
    }

    public async IAsyncEnumerable<TranslationEvent> TranslateTextAsync(
        string text,
        OcrLanguage? sourceLanguage = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var general = _settings.Current.General;
        var source = sourceLanguage is null
            ? _languages.Get(general.SourceLanguage.Id)
            : TryGetTranslationLanguage(sourceLanguage.Id)
              ?? _languages.Get(general.SourceLanguage.Id);
        var request = new TranslationRequest(
            text,
            source,
            _languages.Get(general.TargetLanguage.Id));
        await foreach (var item in _translation.StreamAsync(request, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return item;
        }
    }

    public Task<ImageTranslationResult> TranslateImageAsync(
        ImageFrame image,
        OcrRecognitionResult recognition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(recognition);
        var general = _settings.Current.General;
        return _imageTranslation.TranslateAsync(
            new ImageTranslationRequest(
                image,
                recognition,
                _languages.Get(general.SourceLanguage.Id),
                _languages.Get(general.TargetLanguage.Id)),
            cancellationToken);
    }

    internal static OcrLanguage? ResolveOcrLanguage(string languageId)
    {
        return OcrLanguages.TryGet(languageId, out var language)
            ? language
            : null;
    }

    private TranslationLanguage? TryGetTranslationLanguage(string languageId)
    {
        try
        {
            return _languages.Get(languageId);
        }
        catch
        {
            return null;
        }
    }
}
