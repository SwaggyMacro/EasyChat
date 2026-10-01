using System.Diagnostics;
using Avalonia.Controls.ApplicationLifetimes;
using EasyChat.Contracts.Shell;

namespace EasyChat.Desktop.Windows.ApplicationLifecycle;

internal sealed class WindowsApplicationRestartService : IApplicationRestartService
{
    public void Restart()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
            startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add("--restart");

        if (Process.Start(startInfo) is not null
            && Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
