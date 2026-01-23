using EasyChat.Services.Languages;
using EasyChat.Services.Translation.Machine;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Tests.Services.Translation.Integration;

/// <summary>
///     Tencent Translation API Integration Tests
///     Please fill in your real API credentials below before running the tests
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class TencentTranslationIntegrationTests
{
    // ==========================================
    // Please fill in your Tencent Translation API credentials here
    // ==========================================
    private const string SecretId = "AKIDcEdomX6RXS9dtxpTt55uVs20lJdkeKm6";

    private const string SecretKey = "CAuQk03zrojhnlW484nIcibO0QfimeI1";
    // ==========================================

    private static bool IsConfigured => SecretId != "YOUR_TENCENT_SECRET_ID" && SecretKey != "YOUR_TENCENT_SECRET_KEY";

    [TestMethod]
    public async Task TranslateAsync_EnglishToChinese_ShouldReturnTranslatedText()
    {
        if (!IsConfigured)
        {
            Assert.Inconclusive("Please configure Tencent Translation API credentials first");
            return;
        }

        // Arrange
        using var service = new TencentService(SecretId, SecretKey, null, NullLogger<TencentService>.Instance);
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
            Assert.Inconclusive("Please configure Tencent Translation API credentials first");
            return;
        }

        // Arrange
        using var service = new TencentService(SecretId, SecretKey, null, NullLogger<TencentService>.Instance);
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
            Assert.Inconclusive("Please configure Tencent Translation API credentials first");
            return;
        }

        // Arrange
        using var service = new TencentService(SecretId, SecretKey, null, NullLogger<TencentService>.Instance);
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
}