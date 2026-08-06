using EasyChat.Contracts.ImageTranslation;
using EasyChat.Presentation.Features.Capture;
using EasyChat.Presentation.Features.Settings;
using EasyChat.Presentation.Features.Settings.Prompts;
using EasyChat.Presentation.Features.Input;
using EasyChat.Presentation.Features.Translation;
using EasyChat.Presentation.Features.TextAssist;
using EasyChat.Presentation.Features.SelectionTranslation;
using EasyChat.Contracts.Selection;
using EasyChat.Contracts.Shortcuts;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.ImageTranslation;
using EasyChat.Presentation.Foundation.Localization;
using EasyChat.Presentation.Features.Shortcuts;
using EasyChat.Presentation.Features.ScreenshotOcr;
using EasyChat.Presentation.Features.Speech;
using EasyChat.Presentation.Features.Shell;
using EasyChat.Presentation.Foundation.Navigation;
using EasyChat.Presentation.Foundation.UiHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace EasyChat.Presentation.DependencyInjection;

public static class EasyChatPresentationServiceCollectionExtensions
{
    public static IServiceCollection AddEasyChatPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ISukiDialogManager, SukiDialogManager>();
        services.AddSingleton<ISukiToastManager, SukiToastManager>();
        // Feature code uses IUi*Host; Suki managers remain only for MainWindow host chrome + adapters.
        services.AddSingleton<IUiDialogHost, SukiUiDialogHost>();
        services.AddSingleton<IUiToastHost, SukiUiToastHost>();
        services.AddSingleton<SettingsSession>();
        services.AddSingleton<PageNavigation>();
        services.AddSingleton<TranslationLanguageOptions>();
        services.AddSingleton<CaptureOverlayCoordinator>();
        services.TryAddSingleton<IScreenshotCaptureSession, InProcessScreenshotCaptureSession>();
        services.AddSingleton<IScreenRegionPicker, AvaloniaScreenRegionPicker>();
        services.AddSingleton<ScreenshotCaptureCoordinator>();
        services.AddSingleton<ScreenshotResultCoordinator>();
        services.AddSingleton<ScreenshotOcrWindowCoordinator>();
        services.AddSingleton<SubtitleWindowCoordinator>();
        services.AddSingleton<ITypingWindowFactory, TypingWindowFactory>();
        services.AddSingleton<ITranslationWindowCoordinator, TranslationWindowCoordinator>();
        services.AddSingleton<ITextAssistWindowCoordinator, TextAssistWindowCoordinator>();
        services.AddSingleton<ISelectionInteractionSink, SelectionInteractionSink>();
        services.AddSingleton<ISettingsDialogCoordinator, SukiSettingsDialogCoordinator>();
        services.AddSingleton<IImageTranslationRenderer, AvaloniaImageTranslationRenderer>();
        services.AddSingleton<ScreenshotShortcutAction>();
        services.AddSingleton<IShortcutAction>(provider => provider.GetRequiredService<ScreenshotShortcutAction>());
        services.AddSingleton<IShortcutAction, ScreenshotOcrShortcutAction>();
        services.AddSingleton<IShortcutAction, InputTranslateShortcutAction>();
        services.AddSingleton<IShortcutAction>(provider => new QuickTextAssistShortcutAction(
            "QuickTranslate",
            correction: false,
            provider.GetRequiredService<ISelectedTextUseCases>(),
            provider.GetRequiredService<ITextAssistWindowCoordinator>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<QuickTextAssistShortcutAction>>()));
        services.AddSingleton<IShortcutAction>(provider => new QuickTextAssistShortcutAction(
            "QuickCorrect",
            correction: true,
            provider.GetRequiredService<ISelectedTextUseCases>(),
            provider.GetRequiredService<ITextAssistWindowCoordinator>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<QuickTextAssistShortcutAction>>()));
        services.AddSingleton<IShortcutAction, SelectionTranslateShortcutAction>();
        services.AddSingleton<IShortcutAction, SwitchTranslationProfileShortcutAction>();

        services.AddTransient<NavigationPageViewModel, HomeViewModel>();
        services.AddTransient<NavigationPageViewModel, SettingViewModel>();
        services.AddTransient<NavigationPageViewModel, ShortcutViewModel>();
        services.AddTransient<NavigationPageViewModel, PromptViewModel>();
        services.AddTransient<NavigationPageViewModel, SpeechRecognitionViewModel>();
        services.AddTransient<NavigationPageViewModel, TextAssistViewModel>();
        services.AddTransient<NavigationPageViewModel, AboutViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        return services;
    }
}
