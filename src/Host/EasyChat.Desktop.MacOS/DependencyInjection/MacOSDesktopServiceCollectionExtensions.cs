using Microsoft.Extensions.DependencyInjection;
using EasyChat.Contracts.Shell;
using EasyChat.Desktop.MacOS.ApplicationLifecycle;

namespace EasyChat.Desktop.MacOS.DependencyInjection;

public static class MacOSDesktopServiceCollectionExtensions
{
    public static IServiceCollection AddEasyChatMacOSDesktop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IApplicationRestartService, MacOSApplicationRestartService>();
        return services;
    }
}
