using EasyChat.Services.Languages;
using EasyChat.Services.Translation;
using EasyChat.Services.Translation.Machine;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Tests.Services.Translation.Machine;

[TestClass]
public sealed class BaiduServiceTests
{
    private const string TestAppId = "test-app-id";
    private const string TestSecretKey = "test-secret-key";

    [TestMethod]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Arrange & Act
        using var service = new BaiduService(TestAppId, TestSecretKey, null, NullLogger<BaiduService>.Instance);

        // Assert
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public void Constructor_WithProxy_ShouldCreateInstance()
    {
        // Arrange & Act
        using var service = new BaiduService(TestAppId, TestSecretKey, "http://localhost:8080", NullLogger<BaiduService>.Instance);

        // Assert
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public void StaticLanguageCodes_ShouldBeCorrect()
    {
        // Assert
        Assert.AreEqual("zh", BaiduService.Chinese);
        Assert.AreEqual("en", BaiduService.English);
    }

    [TestMethod]
    public async Task TranslateAsync_WithInvalidCredentials_ShouldReturnErrorResponse()
    {
        // Arrange
        using var service = new BaiduService(TestAppId, TestSecretKey, null, NullLogger<BaiduService>.Instance);

        // Act
        // Act
        var source = LanguageService.GetLanguage("en");
        var target = LanguageService.GetLanguage("zh-Hans");
        var result = await service.TranslateAsync("Hello", source, target, true);

        // Assert
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void ImplementsITranslationInterface()
    {
        // Arrange & Act
        using var service = new BaiduService(TestAppId, TestSecretKey, null, NullLogger<BaiduService>.Instance);

        // Assert - Verify the service implements ITranslation
        Assert.IsInstanceOfType<ITranslation>(service);
    }

    [TestMethod]
    public void ImplementsIDisposableInterface()
    {
        // Arrange & Act
        using var service = new BaiduService(TestAppId, TestSecretKey, null, NullLogger<BaiduService>.Instance);

        // Assert - Verify the service implements IDisposable
        Assert.IsInstanceOfType<IDisposable>(service);
    }

    [TestMethod]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var service = new BaiduService(TestAppId, TestSecretKey, null, NullLogger<BaiduService>.Instance);

        // Act & Assert - Should not throw
        service.Dispose();
    }
}