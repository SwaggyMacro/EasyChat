using EasyChat.Contracts.Ocr;

namespace EasyChat.Application.Ocr;

public sealed class OcrRecognitionUseCases : IOcrRecognitionUseCases
{
    private readonly IOcrRecognizer _recognizer;

    public OcrRecognitionUseCases(IOcrRecognizer recognizer)
    {
        _recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));
    }

    public ValueTask<OcrRecognitionResult> RecognizeAsync(
        OcrRecognitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var language = request.Language;
        var resolvedLanguage = language is not null
            && !string.Equals(language.Id, OcrLanguages.Auto.Id, StringComparison.OrdinalIgnoreCase)
            && OcrLanguages.TryGet(language.Id, out var canonical)
                ? canonical
                : OcrLanguages.ChineseSimplified;
        var resolved = request with { Language = resolvedLanguage };
        return _recognizer.RecognizeAsync(resolved, cancellationToken);
    }
}
