using Microsoft.Extensions.DependencyInjection;
using EasyChat.Contracts.Shell;
using EasyChat.Desktop.MacOS.ApplicationLifecycle;
using EasyChat.Desktop.MacOS.Capture;
using EasyChat.Presentation.Features.Capture;
using EasyChat.Presentation.Foundation.Platform;

namespace EasyChat.Desktop.MacOS.DependencyInjection;

public static class MacOSDesktopServiceCollectionExtensions
{
    public static IServiceCollection AddEasyChatMacOSDesktop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IPlatformWindowBehavior, AvaloniaMacWindowBehavior>();
        services.AddSingleton<IApplicationRestartService, MacOSApplicationRestartService>();
        services.AddSingleton<IScreenshotCaptureSession, MacScreenshotCaptureSession>();
        return services;
    }
}
