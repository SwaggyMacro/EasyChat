using EasyChat.Contracts.ImageTranslation;
using EasyChat.Presentation.Features.Capture;
using EasyChat.Presentation.Features.Capture.Views;
using EasyChat.Presentation.Features.Settings;
using EasyChat.Presentation.Features.Settings.Prompts;
using EasyChat.Presentation.Features.Settings.Prompts.Views;
using EasyChat.Presentation.Features.Settings.Views;
using EasyChat.Presentation.Features.Input;
using EasyChat.Presentation.Features.Translation;
using EasyChat.Presentation.Features.TextAssist;
using EasyChat.Presentation.Features.SelectionTranslation;
using EasyChat.Presentation.Features.ScreenshotOcr;
using EasyChat.Contracts.Selection;
using EasyChat.Contracts.Shortcuts;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Features.Settings.Theme;
using EasyChat.Presentation.Features.Settings.Theme.Views;
using EasyChat.Presentation.Features.Settings.Translation;
using EasyChat.Presentation.Features.Settings.Translation.Views;
using EasyChat.Presentation.ImageTranslation;
using EasyChat.Presentation.Foundation.Localization;
using EasyChat.Presentation.Features.Shortcuts;
using EasyChat.Presentation.Features.Shortcuts.Views;
using EasyChat.Presentation.Features.Speech;
using EasyChat.Presentation.Features.Speech.Views;
using EasyChat.Presentation.Features.Shell;
using EasyChat.Presentation.Features.Shell.Views;
using EasyChat.Presentation.Foundation.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace EasyChat.Presentation.DependencyInjection;

public static class EasyChatPresentationServiceCollectionExtensions
{
    public static IServiceCollection AddEasyChatPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(_ => new ShadUI.DialogManager()
            .Register<CloseBehaviorDialogView, CloseBehaviorDialogViewModel>()
            .Register<CustomThemeDialogView, CustomThemeDialogViewModel>()
            .Register<AiModelEditDialogView, AiModelEditDialogViewModel>()
            .Register<KeyListEditorView, KeyListEditorViewModel>()
            .Register<FixedAreaEditDialogView, FixedAreaEditDialogViewModel>()
            .Register<FixedAreaFormDialogView, FixedAreaFormDialogViewModel>()
            .Register<TtsVoiceSettingsDialogView, TtsVoiceSettingsDialogViewModel>()
            .Register<TtsEditVoiceDialogView, TtsEditVoiceDialogViewModel>()
            .Register<TtsPreviewInputDialogView, TtsPreviewInputDialogViewModel>()
            .Register<ShortcutEditDialogView, ShortcutEditDialogViewModel>()
            .Register<PromptEditDialogView, PromptEditDialogViewModel>()
            .Register<RunningAppPickerDialogView, RunningAppPickerDialogViewModel>()
            .Register<SelectionAppListDialogView, SelectionAppListDialogViewModel>());
        services.AddSingleton<ShadUI.ToastManager>();
        services.AddKeyedSingleton<ShadUI.ToastManager>(
            MainWindowViewModel.UpdateToastManagerKey,
            static (_, _) => new ShadUI.ToastManager());
        services.AddSingleton<SettingsSession>();
        services.AddSingleton<PageNavigation>();
        services.AddSingleton<TranslationLanguageOptions>();
        services.AddSingleton<CaptureOverlayCoordinator>();
        services.AddSingleton<IScreenRegionPicker, AvaloniaScreenRegionPicker>();
        services.AddSingleton<ScreenshotCaptureCoordinator>();
        services.AddSingleton<ScreenshotResultCoordinator>();
        services.AddSingleton<ScreenshotOcrWindowCoordinator>();
        services.AddSingleton<SubtitleWindowCoordinator>();
        services.AddSingleton<SpeechInterpretationHotkeyController>();
        services.AddSingleton<ITypingWindowFactory, TypingWindowFactory>();
        services.AddSingleton<TsfCandidateWindowCoordinator>();
        services.AddSingleton<ITranslationWindowCoordinator, TranslationWindowCoordinator>();
        services.AddSingleton<ITextAssistWindowCoordinator, TextAssistWindowCoordinator>();
        services.AddSingleton<ISelectionInteractionSink, SelectionInteractionSink>();
        services.AddSingleton<ISettingsDialogCoordinator, SettingsDialogCoordinator>();
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
        services.AddSingleton<IShortcutAction, SpeechInterpretationShortcutAction>();

        services.AddTransient<NavigationPageViewModel, HomeViewModel>();
        services.AddTransient<NavigationPageViewModel, SettingViewModel>();
        services.AddTransient<NavigationPageViewModel, ShortcutViewModel>();
        services.AddTransient<NavigationPageViewModel, PromptViewModel>();
        services.AddTransient<NavigationPageViewModel, SpeechRecognitionViewModel>();
        services.AddTransient<NavigationPageViewModel, TextAssistTranslationPageViewModel>();
        services.AddTransient<NavigationPageViewModel, TextAssistCorrectionPageViewModel>();
        services.AddTransient<NavigationPageViewModel, AboutViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        return services;
    }
}
