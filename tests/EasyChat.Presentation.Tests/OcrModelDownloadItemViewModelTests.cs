using EasyChat.Contracts.Ocr;
using EasyChat.Presentation.Features.Settings;

namespace EasyChat.Presentation.Tests;

[TestClass]
public sealed class OcrModelDownloadItemViewModelTests
{
    [TestMethod]
    public void LanguageList_CompactsOnlyLargePackages()
    {
        var largePackage = new OcrModelPackage(
            "universal",
            Enumerable.Range(1, 50)
                .Select(index => new OcrLanguage($"language-{index}", $"Language {index}"))
                .ToArray());
        var smallPackage = new OcrModelPackage(
            "script",
            [new OcrLanguage("ko", "Korean")]);

        var large = new OcrModelDownloadItemViewModel(
            largePackage,
            "Universal",
            string.Empty,
            "all languages",
            isDownloaded: false);
        var small = new OcrModelDownloadItemViewModel(
            smallPackage,
            "Korean",
            string.Empty,
            "Supported languages: Korean",
            isDownloaded: false);

        Assert.IsTrue(large.IsSupportedLanguageListCompact);
        StringAssert.Contains(large.SupportedLanguagesSummary, "50");
        Assert.AreEqual("all languages", large.SupportedLanguages);
        Assert.IsFalse(small.IsSupportedLanguageListCompact);
        Assert.AreEqual(small.SupportedLanguages, small.SupportedLanguagesSummary);
    }
}
