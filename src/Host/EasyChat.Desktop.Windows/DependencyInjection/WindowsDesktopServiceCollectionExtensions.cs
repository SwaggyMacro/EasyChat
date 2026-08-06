using EasyChat.Desktop.Windows;
using EasyChat.Desktop.Windows.Capture;
using EasyChat.Presentation.Features.Capture;
using EasyChat.Presentation.Foundation.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace EasyChat.Desktop.Windows.DependencyInjection;

public static class WindowsDesktopServiceCollectionExtensions
{
    public static IServiceCollection AddEasyChatWindowsDesktop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IPlatformWindowBehavior, AvaloniaWindowsWindowBehavior>();
        services.AddSingleton<IScreenshotCaptureSession, WindowsScreenshotCaptureSession>();
        return services;
    }
}
