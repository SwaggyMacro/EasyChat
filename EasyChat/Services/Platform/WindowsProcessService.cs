using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EasyChat.Models;
using EasyChat.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace EasyChat.Services.Platform;

public class WindowsProcessService : IProcessService
{
    private readonly ILogger<WindowsProcessService> _logger;
    public ObservableCollection<ProcessInfo> Processes { get; } = new();

    public WindowsProcessService(ILogger<WindowsProcessService> logger)
    {
        _logger = logger;
    }

    public void RefreshProcesses()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                var currentSelectedIds = Processes.Where(p => p.IsSelected).Select(p => p.Id).ToHashSet();
                Processes.Clear();

                var global = new ProcessInfo { Id = 0, Name = "Global", Title = "System Audio", IsSelected = currentSelectedIds.Contains(0) || currentSelectedIds.Count == 0 };
                Processes.Add(global);

                var procs = Process.GetProcesses();
                var itemsToLoadIcon = new List<ProcessInfo>();

                foreach (var p in procs)
                {
                    if (p.Id == 0) continue;
                    try
                    {
                        if (!string.IsNullOrEmpty(p.MainWindowTitle))
                        {
                            var pi = new ProcessInfo
                            {
                                Id = p.Id,
                                Name = p.ProcessName,
                                Title = p.MainWindowTitle,
                                IsSelected = currentSelectedIds.Contains(p.Id)
                            };
                            Processes.Add(pi);
                            itemsToLoadIcon.Add(pi);
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                }
                
                // Load Icons Background
                Task.Run(() => LoadIcons(itemsToLoadIcon));
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing processes");
        }
    }

    private void LoadIcons(List<ProcessInfo> items)
    {
        foreach (var pi in items)
        {
            try
            {
                var path = GetProcessPath(pi.Id);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    if (icon != null)
                    {
                        using var sysBitmap = icon.ToBitmap();
                        using var stream = new MemoryStream();
                        sysBitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                        stream.Position = 0;
                        var avaloniaBitmap = new Bitmap(stream);
                        
                        Dispatcher.UIThread.Post(() => pi.AppIcon = avaloniaBitmap);
                    }
                }
            }
            catch
            {
                // Ignore
            }
        }
    }

    // --- P/Invoke Helpers ---
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private string? GetProcessPath(int processId)
    {
        IntPtr hProcess = IntPtr.Zero;
        try
        {
            hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (hProcess != IntPtr.Zero)
            {
                StringBuilder sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                {
                    return sb.ToString();
                }
            }
        }
        catch
        {
            // ignored
        }
        finally
        {
            if (hProcess != IntPtr.Zero) CloseHandle(hProcess);
        }
        return null;
    }
}
