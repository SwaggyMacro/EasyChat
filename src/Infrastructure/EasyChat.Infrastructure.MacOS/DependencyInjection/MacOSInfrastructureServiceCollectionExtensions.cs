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

        return services;
    }
}
