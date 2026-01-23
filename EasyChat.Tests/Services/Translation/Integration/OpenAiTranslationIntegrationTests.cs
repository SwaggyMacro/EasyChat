using EasyChat.Services.Languages;
using EasyChat.Services.Translation.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Tests.Services.Translation.Integration;

/// <summary>
///     OpenAI Translation API Integration Tests
///     Please fill in your real API key below before running the tests
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class OpenAiTranslationIntegrationTests
{
    // ==========================================
    // Please fill in your OpenAI API configuration here
    // ==========================================
    private const string BaseDomain = "https://api.openai.com"; // Or your custom API address
    private const string ApiKey = "YOUR_OPENAI_API_KEY";

    private const string Model = "gpt-4o-mini"; // Optional: gpt-4, gpt-4-turbo, gpt-3.5-turbo etc.
    // ==========================================

    private static bool IsConfigured => ApiKey != "YOUR_OPENAI_API_KEY";

    [TestMethod]
    public async Task TranslateAsync_EnglishToChinese_ShouldReturnTranslatedText()
    {
        if (!IsConfigured)
        {
            Assert.Inconclusive("Please configure OpenAI API Key first");
            return;
        }

        // Arrange
        var service = new OpenAiService(BaseDomain, ApiKey, Model, null, "Translate [SourceLang] to [TargetLang]", NullLogger<OpenAiService>.Instance);
        const string textToTranslate = "Hello, world!";

        // Act
        // Act
        var source = LanguageService.GetLanguage("en");
        var target = LanguageService.GetLanguage("zh-Hans");
        var result = await service.TranslateAsync(textToTranslate, source, target);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result));
        Console.WriteLine($"Translation Result: {textToTranslate} -> {result}");
    }

    [TestMethod]
    public async Task TranslateAsync_ChineseToEnglish_ShouldReturnTranslatedText()
    {
        if (!IsConfigured)
        {
            Assert.Inconclusive("Please configure OpenAI API Key first");
            return;
        }

        // Arrange
        var service = new OpenAiService(BaseDomain, ApiKey, Model, null, "Translate [SourceLang] to [TargetLang]", NullLogger<OpenAiService>.Instance);
        const string textToTranslate = "Hello, world!";

        // Act
        // Act
        var source = LanguageService.GetLanguage("zh-Hans");
        var target = LanguageService.GetLanguage("en");
        var result = await service.TranslateAsync(textToTranslate, source, target);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result));
        Console.WriteLine($"Translation Result: {textToTranslate} -> {result}");
    }

    [TestMethod]
    public async Task TranslateAsync_LongText_ShouldReturnTranslatedText()
    {
        if (!IsConfigured)
        {
            Assert.Inconclusive("Please configure OpenAI API Key first");
            return;
        }

        // Arrange
        var service = new OpenAiService(BaseDomain, ApiKey, Model, null, "Translate [SourceLang] to [TargetLang]", NullLogger<OpenAiService>.Instance);
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
        Console.WriteLine($"Translation Result: {textToTranslate} -> {result}");
    }

    [TestMethod]
    public async Task StreamTranslateAsync_EnglishToChinese_ShouldReturnStreamedText()
    {
        if (!IsConfigured)
        {
            Assert.Inconclusive("Please configure OpenAI API Key first");
            return;
        }

        // Arrange
        var service = new OpenAiService(BaseDomain, ApiKey, Model, null, "Translate [SourceLang] to [TargetLang]", NullLogger<OpenAiService>.Instance);
        const string textToTranslate = "Hello, world!";
        var result = string.Empty;

        // Act
        // Act
        var source = LanguageService.GetLanguage("en");
        var target = LanguageService.GetLanguage("zh-Hans");
        await foreach (var chunk in service.StreamTranslateAsync(textToTranslate, source, target))
        {
            result += chunk;
            Console.Write(chunk); // Real-time stream output
        }

        Console.WriteLine();

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result));
        Console.WriteLine($"Complete Translation Result: {textToTranslate} -> {result}");
    }
}