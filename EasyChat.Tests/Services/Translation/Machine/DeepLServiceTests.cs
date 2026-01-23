using EasyChat.Services.Languages;
using EasyChat.Services.Translation.Machine;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Tests.Services.Translation.Machine;

[TestClass]
public sealed class DeepLServiceTests
{
    private const string TestApiKey = "test-api-key";

    [TestMethod]
    public void Constructor_WithQualityOptimizedModel_ShouldCreateInstance()
    {
        // Arrange & Act
        var service = new DeepLService("quality_optimized", TestApiKey, null, NullLogger<DeepLService>.Instance);

        // Assert
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public void Constructor_WithPreferQualityOptimizedModel_ShouldCreateInstance()
    {
        // Arrange & Act
        var service = new DeepLService("prefer_quality_optimized", TestApiKey, null, NullLogger<DeepLService>.Instance);

        // Assert
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public void Constructor_WithLatencyOptimizedModel_ShouldCreateInstance()
    {
        // Arrange & Act
        var service = new DeepLService("latency_optimized", TestApiKey, null, NullLogger<DeepLService>.Instance);

        // Assert
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public void Constructor_WithUnknownModel_ShouldDefaultToLatencyOptimized()
    {
        // Arrange & Act
        var service = new DeepLService("unknown_model", TestApiKey, null, NullLogger<DeepLService>.Instance);

        // Assert
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public void Constructor_WithProxy_ShouldCreateInstance()
    {
        // Arrange & Act
        var service = new DeepLService("latency_optimized", TestApiKey, "http://localhost:8080", NullLogger<DeepLService>.Instance);

        // Assert
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public async Task TranslateAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        var service = new DeepLService("latency_optimized", TestApiKey, null, NullLogger<DeepLService>.Instance);
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
            // Expected exception
        }
    }
}