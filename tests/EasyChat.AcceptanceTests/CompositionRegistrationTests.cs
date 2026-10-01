using EasyChat.Application.DependencyInjection;
using EasyChat.Application.ImageTranslation;
using EasyChat.Application.Input;
using EasyChat.Application.Ocr;
using EasyChat.Application.Settings;
using EasyChat.Application.Selection;
using EasyChat.Application.SelectionTranslation;
using EasyChat.Application.Shell;
using EasyChat.Application.Speech;
using EasyChat.Application.Translation;
using EasyChat.Application.TextAssist;
using EasyChat.Contracts.ImageTranslation;
using EasyChat.Contracts.Input;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Selection;
using EasyChat.Contracts.SelectionTranslation;
using EasyChat.Contracts.Shell;
using EasyChat.Contracts.Translation;
using EasyChat.Contracts.TextAssist;
using EasyChat.Contracts.Speech;
using EasyChat.Contracts.Platform;
using EasyChat.Desktop.Windows.DependencyInjection;
using EasyChat.Infrastructure.DependencyInjection;
using EasyChat.Infrastructure.Translation;
using EasyChat.Infrastructure.Windows.DependencyInjection;
using EasyChat.Presentation.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.AcceptanceTests;

[TestClass]
public sealed class CompositionRegistrationTests
{
    [TestMethod]
    public async Task CurrentModules_BuildAndResolveToOwnedImplementations()
    {
        if (!OperatingSystem.IsWindows())
        {
            // This composes the Windows module, whose adapters refuse to register anywhere else, so
            // there is nothing meaningful to assert off Windows. The macOS composition is checked by
            // running the bundle with --verify-composition, because that also exercises the real
            // .app identity and native library resolution, which a test host cannot.
            Assert.Inconclusive("The Windows composition can only be verified on Windows.");
            return;
        }

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<ILogger<LoggingTranslationFailureSink>>(
            NullLogger<LoggingTranslationFailureSink>.Instance);
        services.AddLogging();
        services.AddEasyChatInfrastructure(Path.Combine(
            Path.GetTempPath(),
            "EasyChat.RefactorV2.Acceptance",
            "Configuration"));
        services.AddEasyChatWindowsInfrastructure();
        services.AddEasyChatPresentation();
        services.AddEasyChatApplication(new TranslationMessages("request failed"));
        services.AddEasyChatWindowsDesktop();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.IsInstanceOfType<SettingsCoordinator>(
            provider.GetRequiredService<ISettingsUseCases>());
        Assert.IsInstanceOfType<TranslationUseCases>(
            provider.GetRequiredService<ITranslationUseCases>());
        Assert.IsInstanceOfType<ShellLifecycle>(
            provider.GetRequiredService<IShellLifecycle>());
        Assert.IsInstanceOfType<OcrRecognitionUseCases>(
            provider.GetRequiredService<IOcrRecognitionUseCases>());
        Assert.IsInstanceOfType<OcrModelUseCases>(
            provider.GetRequiredService<IOcrModelUseCases>());
        Assert.IsInstanceOfType<ImageTranslationUseCases>(
            provider.GetRequiredService<IImageTranslationUseCases>());
        Assert.IsInstanceOfType<InputDeliveryUseCases>(
            provider.GetRequiredService<IInputDeliveryUseCases>());
        Assert.IsInstanceOfType<BuiltInTranslationLanguageCatalog>(
            provider.GetRequiredService<ITranslationLanguageCatalog>());
        Assert.IsInstanceOfType<SelectedTextUseCases>(
            provider.GetRequiredService<ISelectedTextUseCases>());
        Assert.IsInstanceOfType<SelectionInteractionCoordinator>(
            provider.GetRequiredService<ISelectionInteractionUseCases>());
        Assert.IsInstanceOfType<SelectionTranslationUseCases>(
            provider.GetRequiredService<ISelectionTranslationUseCases>());
        Assert.IsInstanceOfType<TextAssistUseCases>(
            provider.GetRequiredService<ITextAssistUseCases>());
        Assert.IsInstanceOfType<TtsUseCases>(
            provider.GetRequiredService<ITtsUseCases>());
        Assert.IsInstanceOfType<SpeechRecognitionUseCases>(
            provider.GetRequiredService<ISpeechRecognitionUseCases>());
        Assert.IsInstanceOfType<SpeechRecognitionModelDownloadUseCases>(
            provider.GetRequiredService<ISpeechRecognitionModelDownloadUseCases>());
        Assert.IsNotNull(provider.GetRequiredService<IGlobalPointerMonitor>());
        Assert.IsNotNull(provider.GetRequiredService<ISelectedTextCapture>());
        Assert.IsNotNull(provider.GetRequiredService<IAudioCaptureSourceCatalog>());
        Assert.IsNotNull(provider.GetRequiredService<ISpeechRecognitionEngine>());
        Assert.IsNotNull(provider.GetRequiredService<IAudioPlaybackQueue>());
    }
}
