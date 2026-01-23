using EasyChat.Services.Languages;
using EasyChat.Services.Translation.Machine;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Tests.Services.Translation.Integration;

/// <summary>
///     Baidu Translation API Integration Tests
///     Please fill in your real API key below before running the tests
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class BaiduTranslationIntegrationTests
{
    // ==========================================
    // Please fill in your Baidu Translation API Key here
    // ==========================================
    private const string AppId = "20170809000071536";

    private const string SecretKey = "gafCbE2uiD919_K0aIre";
    // ==========================================

    private static bool IsConfigured => AppId != "YOUR_BAIDU_APP_ID" && SecretKey != "YOUR_BAIDU_SECRET_KEY";

    [TestMethod]
    public async Task TranslateAsync_EnglishToChinese_ShouldReturnTranslatedText()
    {
        if (!IsConfigured)
        {
            Assert.Inconclusive("Please configure Baidu Translation API Key first");
            return;
        }

        // Arrange
        using var service = new BaiduService(AppId, SecretKey, null, NullLogger<BaiduService>.Instance);
        const string textToTranslate = "Hello, world!";

        // Act
        // Act
        var source = LanguageService.GetLanguage("en");
        var target = LanguageService.GetLanguage("zh-Hans");
        var result = await service.TranslateAsync(textToTranslate, source, target);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result));
        Assert.DoesNotContain("error", result, $@"Translation returned error: {result}");
        Console.WriteLine($@"Translation Result: {textToTranslate} -> {result}");
    }

    [TestMethod]
    public async Task TranslateAsync_ChineseToEnglish_ShouldReturnTranslatedText()
    {
        if (!IsConfigured)
        {
            Assert.Inconclusive("Please configure Baidu Translation API Key first");
            return;
        }

        // Arrange
        using var service = new BaiduService(AppId, SecretKey, null, NullLogger<BaiduService>.Instance);
        const string textToTranslate = "Hello, world!";

        // Act
        // Act
        var source = LanguageService.GetLanguage("zh-Hans");
        var target = LanguageService.GetLanguage("en");
        var result = await service.TranslateAsync(textToTranslate, source, target);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result));
        Assert.DoesNotContain("error", result, $"Translation returned error: {result}");
        Console.WriteLine($@"Translation Result: {textToTranslate} -> {result}");
    }

    [TestMethod]
    public async Task TranslateAsync_LongText_ShouldReturnTranslatedText()
    {
        if (!IsConfigured)
        {
            Assert.Inconclusive("Please configure Baidu Translation API Key first");
            return;
        }

        // Arrange
        using var service = new BaiduService(AppId, SecretKey, null, NullLogger<BaiduService>.Instance);
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
        Assert.DoesNotContain("error", result, $"Translation returned error: {result}");
        Console.WriteLine($@"Translation Result: {textToTranslate} -> {result}");
    }
}