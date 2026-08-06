using EasyChat.Application.ApplicationData;
using EasyChat.Application.Capture;
using EasyChat.Application.ImageTranslation;
using EasyChat.Application.Input;
using EasyChat.Application.Ocr;
using EasyChat.Application.Platform;
using EasyChat.Application.Settings;
using EasyChat.Application.Selection;
using EasyChat.Application.SelectionTranslation;
using EasyChat.Application.Shortcuts;
using EasyChat.Application.Shell;
using EasyChat.Application.Speech;
using EasyChat.Application.Translation;
using EasyChat.Application.TextAssist;
using EasyChat.Contracts.ApplicationData;
using EasyChat.Contracts.Capture;
using EasyChat.Contracts.ImageTranslation;
using EasyChat.Contracts.Input;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Selection;
using EasyChat.Contracts.SelectionTranslation;
using EasyChat.Contracts.Shortcuts;
using EasyChat.Contracts.Shell;
using EasyChat.Contracts.Speech;
using EasyChat.Contracts.Translation;
using EasyChat.Contracts.TextAssist;
using Microsoft.Extensions.DependencyInjection;

namespace EasyChat.Application.DependencyInjection;

public static class EasyChatApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddEasyChatApplication(
        this IServiceCollection services,
        TranslationMessages translationMessages)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(translationMessages);

        services.AddSingleton(translationMessages);
        services.AddSingleton<IApplicationDataUseCases, ApplicationDataUseCases>();
        services.AddSingleton<IPlatformAccessUseCases, PlatformAccessUseCases>();
        services.AddSingleton<ISettingsUseCases, SettingsCoordinator>();
        services.AddSingleton<ITranslationLanguageCatalog, BuiltInTranslationLanguageCatalog>();
        services.AddSingleton<ITranslationUseCases, TranslationUseCases>();
        services.AddSingleton<ISelectionTranslationUseCases, SelectionTranslationUseCases>();
        services.AddSingleton<ITextAssistUseCases, TextAssistUseCases>();
        services.AddSingleton<ITtsUseCases, TtsUseCases>();
        services.AddSingleton<ISpeechRecognitionUseCases, SpeechRecognitionUseCases>();
        services.AddSingleton<IScreenshotUseCases, ScreenshotUseCases>();
        services.AddSingleton<IOcrRecognitionUseCases, OcrRecognitionUseCases>();
        services.AddSingleton<IOcrModelUseCases, OcrModelUseCases>();
        services.AddSingleton<IImageTranslationUseCases, ImageTranslationUseCases>();
        services.AddSingleton<ImageTranslationMemoryBudget>();
        services.AddSingleton<IImageTranslationEditSessionFactory, ImageTranslationEditSessionFactory>();
        services.AddSingleton<IInputDeliveryUseCases, InputDeliveryUseCases>();
        services.AddSingleton<IInputTranslationUseCases, InputTranslationUseCases>();
        services.AddSingleton<ISelectedTextUseCases, SelectedTextUseCases>();
        services.AddSingleton<ISelectionInteractionUseCases, SelectionInteractionCoordinator>();
        services.AddSingleton<IShortcutUseCases, ShortcutCoordinator>();
        services.AddSingleton<IShellLifecycle, ShellLifecycle>();
        return services;
    }
}
