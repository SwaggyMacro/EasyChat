using EasyChat.Contracts.ImageTranslation;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.ApplicationStartup;
using EasyChat.Infrastructure.MacOS.Audio;
using EasyChat.Infrastructure.MacOS.Capture;
using EasyChat.Infrastructure.MacOS.ImageTranslation;
using EasyChat.Infrastructure.MacOS.Input;
using EasyChat.Infrastructure.MacOS.Ocr;
using Microsoft.Extensions.DependencyInjection;

namespace EasyChat.Infrastructure.MacOS.DependencyInjection;

public static class MacOSInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddEasyChatMacOSInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
            throw new PlatformNotSupportedException(
                "The macOS infrastructure module requires macOS 26 or later.");

        services.AddSingleton<IPlatformCapabilities, MacPlatformCapabilities>();
        services.AddSingleton<IPlatformPermissionRequester, MacPlatformPermissionRequester>();
        services.AddSingleton<IApplicationAutoStartService, MacApplicationAutoStartService>();
        services.AddSingleton<MacOwnedWindowBehavior>();

        // One pasteboard instance is shared by all three clipboard ports so a capture, a temporary
        // write and a restore can never interleave.
        services.AddSingleton<MacPasteboard>();
        services.AddSingleton<IClipboardSnapshots, MacClipboardSnapshots>();
        services.AddSingleton<IClipboardText, MacClipboardText>();
        services.AddSingleton<IClipboardImage, MacClipboardImage>();

        services.AddSingleton<IWindowFocus, MacWindowFocus>();
        services.AddSingleton<IRunningProcessCatalog, MacRunningProcessCatalog>();
        services.AddSingleton<ITextSelection, MacTextSelection>();
        services.AddSingleton<ITextDelivery, MacTextDelivery>();
        services.AddSingleton<MacGlobalHotkeys>();
        services.AddSingleton<IGlobalHotkeys>(provider => provider.GetRequiredService<MacGlobalHotkeys>());
        services.AddSingleton<IHoldGlobalHotkeys>(provider => provider.GetRequiredService<MacGlobalHotkeys>());
        services.AddSingleton<IKeyboardState, MacKeyboardState>();
        services.AddSingleton<IPointerPosition, MacPointerPosition>();
        services.AddSingleton<IScreenCatalog, MacScreenCatalog>();
        services.AddSingleton<IScreenCapture, MacScreenCapture>();
        services.AddSingleton<IAudioCaptureSourceCatalog, MacAudioCaptureSourceCatalog>();
        services.AddSingleton<IAudioPlaybackDeviceCatalog, MacAudioPlaybackDeviceCatalog>();
        services.AddSingleton<IAudioPlaybackQueue, MacAudioPlaybackQueue>();
        services.AddSingleton<IAudioFeedbackCuePlayer, MacAudioFeedbackCuePlayer>();
        services.AddSingleton<IMacAudioSourceFactory, MacAudioSourceFactory>();
        services.AddSingleton<MacPcmAudioCapture>();
        services.AddSingleton<IPcmAudioCapture>(provider => provider.GetRequiredService<MacPcmAudioCapture>());
        services.AddSingleton<IPreparablePcmAudioCapture>(provider => provider.GetRequiredService<MacPcmAudioCapture>());
        services.AddSingleton<IGlobalPointerMonitor, MacGlobalPointerMonitor>();
        services.AddSingleton<ISelectedTextCapture, MacSelectedTextCapture>();

        // OCR and image text removal are suspended (phases 10 and 11); these fail explicitly so the
        // rest of the app can compose and run.
        services.AddSingleton<MacUnavailableOcr>();
        services.AddSingleton<IOcrRecognizer>(provider => provider.GetRequiredService<MacUnavailableOcr>());
        services.AddSingleton<IOcrModelStore>(provider => provider.GetRequiredService<MacUnavailableOcr>());
        services.AddSingleton<MacUnavailableImageTranslation>();
        services.AddSingleton<IImageBackgroundCleaner>(provider =>
            provider.GetRequiredService<MacUnavailableImageTranslation>());
        services.AddSingleton<IImageTranslationModelStore>(provider =>
            provider.GetRequiredService<MacUnavailableImageTranslation>());
        return services;
    }
}
