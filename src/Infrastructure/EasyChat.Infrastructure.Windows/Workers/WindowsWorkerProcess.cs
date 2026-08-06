using System.Diagnostics;

namespace EasyChat.Infrastructure.Windows.Workers;

internal static class WindowsWorkerProcess
{
    internal static Process Start(string workerArgument, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerArgument);
        ArgumentNullException.ThrowIfNull(arguments);

        var executable = Environment.ProcessPath
                         ?? throw new InvalidOperationException(
                             "Unable to locate the EasyChat executable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add(workerArgument);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return Process.Start(startInfo)
               ?? throw new InvalidOperationException("Unable to start the EasyChat worker process.");
    }

    internal static bool TryWaitForExit(Process process, int milliseconds)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            return process.HasExited || process.WaitForExit(milliseconds);
        }
        catch
        {
            return true;
        }
    }

    internal static void TryTerminate(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Worker cleanup is best effort.
        }
    }
}
