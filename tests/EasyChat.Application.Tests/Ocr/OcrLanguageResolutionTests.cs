using EasyChat.Application.Capture;
using EasyChat.Application.Ocr;
using EasyChat.Application.Translation;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;

namespace EasyChat.Application.Tests.Ocr;

[TestClass]
public sealed class OcrLanguageResolutionTests
{
    private static readonly ImageFrame Frame =
        new(1, 1, 4, 96, 96, new byte[] { 0, 0, 0, 255 });

    [TestMethod]
    public void ScreenshotResolver_MapsEverySupportedSourceIdToCanonicalLanguage()
    {
        foreach (var language in OcrLanguages.Supported)
        {
            var resolved = ScreenshotUseCases.ResolveOcrLanguage(language.Id);
            Assert.IsNotNull(resolved, language.Id);
            Assert.AreEqual(language.Id, resolved.Id, language.Id);
        }
    }

    [TestMethod]
    public void SourceLanguageCatalog_ContainsEveryCanonicalOcrLanguageId()
    {
        var sourceLanguageIds = new BuiltInTranslationLanguageCatalog()
            .All
            .Select(language => language.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var language in OcrLanguages.Supported)
            Assert.Contains(language.Id, sourceLanguageIds, language.Id);
    }

    [TestMethod]
    public void ScreenshotResolver_MapsLegacySerbianAndLeavesUnknownForDefaultPolicy()
    {
        Assert.AreEqual("sr-Cyrl", ScreenshotUseCases.ResolveOcrLanguage("sr")?.Id);
        Assert.AreEqual(OcrLanguages.Auto.Id, ScreenshotUseCases.ResolveOcrLanguage("auto")?.Id);
        Assert.IsNull(ScreenshotUseCases.ResolveOcrLanguage("unsupported"));
    }

    [DataRow(null)]
    [DataRow("auto")]
    [DataRow("unsupported")]
    [TestMethod]
    public async Task RecognitionUseCases_DefaultsUnresolvedLanguagesToUniversalCanonicalLanguage(string? id)
    {
        var recognizer = new CapturingRecognizer();
        var useCases = new OcrRecognitionUseCases(recognizer);
        var language = id is null ? null : new OcrLanguage(id, id);

        await useCases.RecognizeAsync(new OcrRecognitionRequest(Frame, language));

        Assert.AreEqual(OcrLanguages.ChineseSimplified.Id, recognizer.Language?.Id);
    }

    private sealed class CapturingRecognizer : IOcrRecognizer
    {
        public OcrLanguage? Language { get; private set; }

        public ValueTask<OcrRecognitionResult> RecognizeAsync(
            OcrRecognitionRequest request,
            CancellationToken cancellationToken = default)
        {
            Language = request.Language;
            return ValueTask.FromResult(new OcrRecognitionResult([]));
        }
    }
}
