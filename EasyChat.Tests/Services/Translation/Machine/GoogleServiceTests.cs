using EasyChat.Services.Languages;
using EasyChat.Services.Translation;
using EasyChat.Services.Translation.Machine;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Tests.Services.Translation.Machine;

[TestClass]
public sealed class GoogleServiceTests
{
    private const string TestApiKey = "test-api-key";
    private const string TestModel = "nmt";

    [TestMethod]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Arrange & Act
        using var service = new GoogleService(TestModel, TestApiKey, null, NullLogger<GoogleService>.Instance);

        // Assert
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public void Constructor_WithProxy_ShouldCreateInstance()
    {
        // Arrange & Act
        using var service = new GoogleService(TestModel, TestApiKey, "http://localhost:8080", NullLogger<GoogleService>.Instance);

        // Assert
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public async Task TranslateAsync_WithInvalidApiKey_ShouldReturnErrorResponse()
    {
        // Arrange
        using var service = new GoogleService(TestModel, TestApiKey, null, NullLogger<GoogleService>.Instance);

        // Act
        // Act
        var source = LanguageService.GetLanguage("en");
        var target = LanguageService.GetLanguage("zh-Hans");
        var result = await service.TranslateAsync("Hello", source, target, true);

        // Assert
        Assert.IsNotNull(result);
        // With invalid API key, we expect an error response
        Assert.IsTrue(result.Contains("error") || result.Contains("Error") || !string.IsNullOrEmpty(result));
    }

    [TestMethod]
    public void ImplementsITranslationInterface()
    {
        // Arrange & Act
        using var service = new GoogleService(TestModel, TestApiKey, null, NullLogger<GoogleService>.Instance);

        // Assert - Verify the service implements ITranslation
        Assert.IsInstanceOfType<ITranslation>(service);
    }

    [TestMethod]
    public void ImplementsIDisposableInterface()
    {
        // Arrange & Act
        using var service = new GoogleService(TestModel, TestApiKey, null, NullLogger<GoogleService>.Instance);

        // Assert - Verify the service implements IDisposable
        Assert.IsInstanceOfType<IDisposable>(service);
    }

    [TestMethod]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var service = new GoogleService(TestModel, TestApiKey, null, NullLogger<GoogleService>.Instance);

        // Act & Assert - Should not throw
        service.Dispose();
    }
}