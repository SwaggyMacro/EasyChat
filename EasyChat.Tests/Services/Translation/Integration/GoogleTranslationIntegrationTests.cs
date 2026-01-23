using EasyChat.Services.Languages;
using EasyChat.Services.Translation.Machine;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Tests.Services.Translation.Integration;

/// <summary>
///     Google Translation API Integration Tests
///     Please fill in your real API key below before running the tests
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class GoogleTranslationIntegrationTests
{
    // ==========================================
    // Please fill in your Google Translation API Key here
    // ==========================================
    private const string ApiKey = "AIzaSyDdKJfZSm_k5aFMbT8gieir4K4v2JqaBdc";

    private const string Model = "nmt"; // Use Neural Machine Translation model
    // ==========================================

    private static bool IsConfigured => ApiKey != "YOUR_GOOGLE_API_KEY";

    [TestMethod]
    public async Task TranslateAsync_EnglishToChinese_ShouldReturnTranslatedText()
    {
        if (!IsConfigured)
        {
            Assert.Inconclusive("Please configure Google Translation API Key first");
            return;
        }

        // Arrange
        using var service = new GoogleService(Model, ApiKey, null, NullLogger<GoogleService>.Instance);
        const string textToTranslate = "Hello, world!";

        // Act
        // Act
        var source = LanguageService.GetLanguage("en");
        var target = LanguageService.GetLanguage("zh-Hans");
        var result = await service.TranslateAsync(textToTranslate, source, target);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result));
        Assert.DoesNotContain("error", result, $"Translation returned error: {result}");
        Console.WriteLine($@"Translation Result: {textToTranslate} -> {result}");
    }

    [TestMethod]
    public async Task TranslateAsync_ChineseToEnglish_ShouldReturnTranslatedText()
    {
        if (!IsConfigured)
        {
            Assert.Inconclusive("Please configure Google Translation API Key first");
            return;
        }

        // Arrange
        using var service = new GoogleService(Model, ApiKey, null, NullLogger<GoogleService>.Instance);
        const string textToTranslate = "Hello, world!";

        // Act
        // Act
        var source = LanguageService.GetLanguage("zh-Hans");
        var target = LanguageService.GetLanguage("en");
        var result = await service.TranslateAsync(textToTranslate, source, target);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result));
        Assert.IsFalse(result.Contains("error"), $"Translation returned error: {result}");
        Console.WriteLine($"Translation Result: {textToTranslate} -> {result}");
    }

    [TestMethod]
    public async Task TranslateAsync_LongText_ShouldReturnTranslatedText()
    {
        if (!IsConfigured)
        {
            Assert.Inconclusive("Please configure Google Translation API Key first");
            return;
        }

        // Arrange
        using var service = new GoogleService(Model, ApiKey, null, NullLogger<GoogleService>.Instance);
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
        Assert.IsFalse(result.Contains("error"), $"Translation returned error: {result}");
        Console.WriteLine($"Translation Result: {textToTranslate} -> {result}");
    }
}