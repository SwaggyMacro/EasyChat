using Avalonia;
using EasyChat.Desktop.Windows.Capture;
using EasyChat.Desktop.Windows.DependencyInjection;
using EasyChat.Infrastructure.Windows.DependencyInjection;
using EasyChat.Infrastructure.Windows.ImageTranslation;
using EasyChat.Infrastructure.Windows.Input;
using EasyChat.Infrastructure.Windows.Ocr;

namespace EasyChat.Desktop.Windows;

internal static class Program
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length >= 2 && string.Equals(
                args[0],
                "--screenshot-worker",
                StringComparison.Ordinal))
        {
            WindowsScreenshotWorker.Run(args[1]);
            return;
        }

        if (args.Length >= 2 && string.Equals(args[0], "--clipboard-worker", StringComparison.Ordinal))
        {
            WindowsClipboardWorker.Run(args[1]);
            return;
        }

        if (args.Length >= 2 && string.Equals(args[0], "--ocr-worker", StringComparison.Ordinal))
        {
            var persistent = args.Length >= 3
                             && string.Equals(args[2], "--persistent", StringComparison.Ordinal);
            WindowsOcrWorker.Run(args[1], persistent);
            return;
        }

        if (args.Length >= 2
            && string.Equals(args[0], "--image-cleaner-worker", StringComparison.Ordinal))
        {
            WindowsImageBackgroundCleanerWorker.Run(args[1]);
            return;
        }

        DesktopApplication.Run(
            args,
            services =>
            {
                services.AddEasyChatWindowsInfrastructure();
                services.AddEasyChatWindowsDesktop();
            },
            () =>
            {
                Velopack.VelopackApp.Build().Run();
                WindowsImeCompositionHook.Start();
            },
            builder => builder
                .With(new SkiaOptions
                {
                    MaxGpuResourceSizeBytes = 16L * 1024 * 1024
                }));
    }
}
