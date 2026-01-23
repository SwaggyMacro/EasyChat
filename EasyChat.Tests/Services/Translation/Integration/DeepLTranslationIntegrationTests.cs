using EasyChat.Services.Languages;
using EasyChat.Services.Translation.Machine;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Tests.Services.Translation.Integration;

/// <summary>
///     DeepL Translation API Integration Tests
///     Please fill in your real API key below before running the tests
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class DeepLTranslationIntegrationTests
{
    // ==========================================
    // Please fill in your DeepL API Key here
    // ==========================================
    private const string ApiKey = "YOUR_DEEPL_API_KEY";

    private const string
        ModelType = "latency_optimized"; // Optional: quality_optimized, prefer_quality_optimized, latency_optimized
    // ==========================================

    private static bool IsConfigured => ApiKey != "YOUR_DEEPL_API_KEY";

    [TestMethod]
    public async Task TranslateAsync_EnglishToChinese_ShouldReturnTranslatedText()
    {
        if (!IsConfigured)
        {
            Assert.Inconclusive("Please configure DeepL API Key first");
            return;
        }

        // Arrange
        var service = new DeepLService(ModelType, ApiKey, null, NullLogger<DeepLService>.Instance);
        const string textToTranslate = "Hello, world!";

        // Act
        // Act
        var source = LanguageService.GetLanguage("en");
        var target = LanguageService.GetLanguage("zh-Hans");
        var result = await service.TranslateAsync(textToTranslate, source, target);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result));
        Console.WriteLine($@"Translation Result: {textToTranslate} -> {result}");
    }

    [TestMethod]
    public async Task TranslateAsync_ChineseToEnglish_ShouldReturnTranslatedText()
    {
        if (!IsConfigured)
        {
            Assert.Inconclusive("Please configure DeepL API Key first");
            return;
        }

        // Arrange
        var service = new DeepLService(ModelType, ApiKey, null, NullLogger<DeepLService>.Instance);
        const string textToTranslate = "Hello, world!";

        // Act
        // Act
        var source = LanguageService.GetLanguage("zh-Hans");
        var target = LanguageService.GetLanguage("en");
        var result = await service.TranslateAsync(textToTranslate, source, target);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result));
        Console.WriteLine($@"Translation Result: {textToTranslate} -> {result}");
    }

    [TestMethod]
    public async Task TranslateAsync_LongText_ShouldReturnTranslatedText()
    {
        if (!IsConfigured)
        {
            Assert.Inconclusive("Please configure DeepL API Key first");
            return;
        }

        // Arrange
        var service = new DeepLService(ModelType, ApiKey, null, NullLogger<DeepLService>.Instance);
        const string textToTranslate =
            "Artificial intelligence is transforming the way we live and work. From smart assistants to autonomous vehicles, AI is becoming an integral part of our daily lives.";

        // Act
        // Act
        var source = LanguageService.GetLanguage("en");
        var target = LanguageService.GetLanguage("zh-Hans");
        var result = await service.TranslateAsync(textToTranslate, source, target);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result));
        Console.WriteLine($@"Translation Result: {textToTranslate} -> {result}");
    }
}