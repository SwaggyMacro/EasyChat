using System;
using Avalonia;
using Avalonia.ReactiveUI;

namespace EasyChat;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Velopack.VelopackApp.Build()
            .Run();

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }
    
    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
    }
}