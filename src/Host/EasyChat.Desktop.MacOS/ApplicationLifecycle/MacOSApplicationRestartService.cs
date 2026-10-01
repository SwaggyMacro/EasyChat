using System.Diagnostics;
using Avalonia.Controls.ApplicationLifetimes;
using EasyChat.Contracts.Shell;

namespace EasyChat.Desktop.MacOS.ApplicationLifecycle;

internal sealed class MacOSApplicationRestartService : IApplicationRestartService
{
    public void Restart()
    {
        var bundlePath = FindApplicationBundle(Environment.ProcessPath);
        if (bundlePath is null)
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/open",
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-n");
        startInfo.ArgumentList.Add(bundlePath);
        startInfo.ArgumentList.Add("--args");
        foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
            startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add("--restart");

        if (Process.Start(startInfo) is not null
            && Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    internal static string? FindApplicationBundle(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
            return null;

        var directory = new DirectoryInfo(Path.GetDirectoryName(processPath)!);
        while (directory is not null)
        {
            if (directory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return directory.FullName;
            directory = directory.Parent;
        }

        return null;
    }
}
