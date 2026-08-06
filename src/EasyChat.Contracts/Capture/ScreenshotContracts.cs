using EasyChat.Contracts.ImageTranslation;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Translation;

namespace EasyChat.Contracts.Capture;

public interface IScreenshotUseCases
{
    OcrLanguage ResolveOcrLanguage(OcrLanguage? requestedLanguage = null);

    ValueTask<OcrRecognitionResult> RecognizeAsync(
        ImageFrame image,
        bool enableRotation,
        OcrLanguage? language = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<TranslationEvent> TranslateTextAsync(
        string text,
        OcrLanguage? sourceLanguage = null,
        CancellationToken cancellationToken = default);

    Task<ImageTranslationResult> TranslateImageAsync(
        ImageFrame image,
        OcrRecognitionResult recognition,
        CancellationToken cancellationToken = default);
}
