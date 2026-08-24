using EasyChat.Contracts.Capture;
using EasyChat.Contracts.ImageTranslation;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.Windows.ApplicationStartup;
using EasyChat.Infrastructure.Windows.Capture;
using EasyChat.Infrastructure.Windows.Audio;
using EasyChat.Infrastructure.Windows.ImageTranslation;
using EasyChat.Infrastructure.Windows.Hotkeys;
using EasyChat.Infrastructure.Windows.Input;
using EasyChat.Infrastructure.Windows.Ocr;
using EasyChat.Infrastructure.Windows.Speech;
using Microsoft.Extensions.DependencyInjection;

namespace EasyChat.Infrastructure.Windows.DependencyInjection;

public static class WindowsInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddEasyChatWindowsInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "The Windows infrastructure module can only be registered on Windows.");

        services.AddSingleton<IPlatformCapabilities, WindowsPlatformCapabilities>();
        services.AddSingleton<IPlatformPermissionRequester, WindowsPlatformPermissionRequester>();
        services.AddSingleton<IApplicationAutoStartService, WindowsApplicationAutoStartService>();
        services.AddSingleton<IScreenCapture, WindowsScreenCapture>();
        services.AddSingleton<IScreenCatalog, WindowsScreenCatalog>();
        services.AddSingleton<ILongScreenshotStitcher, OpenCvLongScreenshotStitcher>();
        services.AddSingleton<IGlobalHotkeys, WindowsGlobalHotkeys>();
        services.AddSingleton<IWindowFocus, WindowsWindowFocus>();
        services.AddSingleton<IWindowInputTransparency, WindowsWindowInputTransparency>();
        services.AddSingleton<IRunningProcessCatalog, WindowsRunningProcessCatalog>();
        services.AddSingleton<WindowsOwnedWindowBehavior>();
        services.AddSingleton<IGlobalPointerMonitor, WindowsGlobalPointerMonitor>();
        services.AddSingleton<IPointerPosition, WindowsPointerPosition>();
        services.AddSingleton<IKeyboardState, WindowsKeyboardState>();
        services.AddSingleton<ITextSelection, WindowsTextSelection>();
        services.AddSingleton<WindowsClipboardSnapshots>();
        services.AddSingleton<IClipboardSnapshots>(provider =>
            provider.GetRequiredService<WindowsClipboardSnapshots>());
        services.AddSingleton<IClipboardText, WindowsClipboardText>();
        services.AddSingleton<IClipboardImage, WindowsClipboardImage>();
        services.AddSingleton<ITextDelivery, WindowsTextDelivery>();
        services.AddSingleton<ISelectedTextCapture, WindowsSelectedTextCapture>();
        services.AddSingleton<IWindowsTsfRegistration, WindowsTsfRegistration>();
        services.AddSingleton<ITextServicesFrameworkBridge, WindowsTsfInputBridge>();
        services.AddSingleton<IAudioCaptureSourceCatalog, WindowsAudioCaptureSourceCatalog>();
        services.AddSingleton<WindowsPcmAudioCapture>();
        services.AddSingleton<IPcmAudioCapture>(provider =>
            provider.GetRequiredService<WindowsPcmAudioCapture>());
        services.AddSingleton<IAudioPlaybackDeviceCatalog, WindowsAudioPlaybackDeviceCatalog>();
        services.AddSingleton<IAudioPlaybackQueue, WindowsSoundFlowAudioPlaybackQueue>();
        services.AddSingleton<IAudioFeedbackCuePlayer, WindowsAudioFeedbackCuePlayer>();
        services.AddSingleton<WindowsImageTranslationModelStore>();
        services.AddSingleton<IImageTranslationModelStore>(provider =>
            provider.GetRequiredService<WindowsImageTranslationModelStore>());
        services.AddSingleton<IImageBackgroundCleaner, WindowsImageBackgroundCleaner>();
        services.AddSingleton<WindowsOpenVinoOcr>();
        services.AddSingleton<IOcrRecognizer>(provider =>
            provider.GetRequiredService<WindowsOpenVinoOcr>());
        services.AddSingleton<IOcrModelStore>(provider =>
            provider.GetRequiredService<WindowsOpenVinoOcr>());
        return services;
    }
}
