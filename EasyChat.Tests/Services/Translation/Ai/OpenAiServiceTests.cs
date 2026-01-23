using EasyChat.Services.Languages;
using EasyChat.Services.Translation.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Tests.Services.Translation.Ai;

[TestClass]
public sealed class OpenAiServiceTests
{
    private const string TestApiUrl = "https://api.siliconflow.cn/v1";
    private const string TestApiKey = "sk-key";
    private const string TestModel = "THUDM/GLM-4.1V-9B-Thinking";

    [TestMethod]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Arrange & Act
        var service = new OpenAiService(TestApiUrl, TestApiKey, TestModel, null, "Translate [SourceLang] to [TargetLang]", NullLogger<OpenAiService>.Instance);
        var source = LanguageService.GetLanguage("en");
        var target = LanguageService.GetLanguage("zh-Hans");
        var result = service.TranslateAsync("Hello", source, target).Result;
        Console.WriteLine(result);
        
        // Assert
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public void Constructor_WithProxy_ShouldCreateInstance()
    {
        // Arrange & Act
        var service = new OpenAiService(TestApiUrl, TestApiKey, TestModel, "http://127.0.0.1:10801", "Translate [SourceLang] to [TargetLang]", NullLogger<OpenAiService>.Instance);

        // Test translation
        // Test translation
        var source = LanguageService.GetLanguage("en");
        var target = LanguageService.GetLanguage("zh-Hans");
        var result = service.TranslateAsync("Hello", source, target).Result;
        Console.WriteLine(result);
        // Assert
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public void Constructor_WithCustomBaseDomain_ShouldCreateInstance()
    {
        // Arrange & Act
        var service = new OpenAiService("https://custom-api.example.com", TestApiKey, TestModel, null, "Translate [SourceLang] to [TargetLang]", NullLogger<OpenAiService>.Instance);

        // Assert
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public async Task TranslateAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        var service = new OpenAiService(TestApiUrl, TestApiKey, TestModel, null, "Translate [SourceLang] to [TargetLang]", NullLogger<OpenAiService>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        try
        {
            var source = LanguageService.GetLanguage("en");
            var target = LanguageService.GetLanguage("zh-Hans");
            await service.TranslateAsync("Hello", source, target, cancellationToken: cts.Token);
            Assert.Fail("Expected TaskCanceledException was not thrown");
        }
        catch (TaskCanceledException)
        {
        }
    }


    [TestMethod]
    public async Task StreamTranslateAsync_ShouldReturnAsyncEnumerable()
    {
        // Arrange
        var service = new OpenAiService(TestApiUrl, TestApiKey, TestModel, null, "Translate [SourceLang] to [TargetLang]", NullLogger<OpenAiService>.Instance);

        // Act
        // Act
        var source = LanguageService.GetLanguage("en");
        var target = LanguageService.GetLanguage("zh-Hans");
        var stream = service.StreamTranslateAsync("Hello", source, target);

        // Assert
        Assert.IsNotNull(stream);
    }
}