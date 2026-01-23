using EasyChat.Services.Languages;
using EasyChat.Services.Translation;
using EasyChat.Services.Translation.Machine;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Tests.Services.Translation.Machine;

[TestClass]
public sealed class TencentServiceTests
{
    private const string TestSecretId = "test-secret-id";
    private const string TestSecretKey = "test-secret-key";

    [TestMethod]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Arrange & Act
        using var service = new TencentService(TestSecretId, TestSecretKey, null, NullLogger<TencentService>.Instance);

        // Assert
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public void Constructor_WithProxy_ShouldCreateInstance()
    {
        // Arrange & Act
        using var service = new TencentService(TestSecretId, TestSecretKey, "http://localhost:8080", NullLogger<TencentService>.Instance);

        // Assert
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public void StaticLanguageCodes_ShouldBeCorrect()
    {
        // Assert
        Assert.AreEqual("zh", TencentService.Chinese);
        Assert.AreEqual("en", TencentService.English);
    }

    [TestMethod]
    public async Task TranslateAsync_WithInvalidCredentials_ShouldReturnResponse()
    {
        // Arrange
        using var service = new TencentService(TestSecretId, TestSecretKey, null, NullLogger<TencentService>.Instance);

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
        using var service = new TencentService(TestSecretId, TestSecretKey, null, NullLogger<TencentService>.Instance);

        // Assert - Verify the service implements ITranslation
        Assert.IsInstanceOfType<ITranslation>(service);
    }

    [TestMethod]
    public void ImplementsIDisposableInterface()
    {
        // Arrange & Act
        using var service = new TencentService(TestSecretId, TestSecretKey, null, NullLogger<TencentService>.Instance);

        // Assert - Verify the service implements IDisposable
        Assert.IsInstanceOfType<IDisposable>(service);
    }

    [TestMethod]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var service = new TencentService(TestSecretId, TestSecretKey, null, NullLogger<TencentService>.Instance);

        // Act & Assert - Should not throw
        service.Dispose();
    }
}