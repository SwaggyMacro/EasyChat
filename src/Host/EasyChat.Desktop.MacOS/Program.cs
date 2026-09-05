using EasyChat.Desktop.MacOS.DependencyInjection;
using EasyChat.Desktop.MacOS.ApplicationLifecycle;
using EasyChat.Infrastructure.MacOS.DependencyInjection;

namespace EasyChat.Desktop.MacOS;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        DesktopApplication.Run(
            args,
            new MacOSDesktopInstanceCoordinator(),
            services =>
            {
                services.AddEasyChatMacOSInfrastructure();
                services.AddEasyChatMacOSDesktop();
            });
    }
}
