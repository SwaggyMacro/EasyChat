using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform; // Try adding this
using Microsoft.Extensions.Logging;

namespace EasyChat.Common;

public static class ClipboardHelper
{
    /// <summary>
    /// Backs up the current clipboard content.
    /// </summary>
    public static async Task<Dictionary<string, object>> BackupClipboardAsync(ILogger? logger = null)
    {
        var backedUpData = new Dictionary<string, object>();
        try
        {
            var clipboard = GetClipboard();
            if (clipboard == null) return backedUpData;

#pragma warning disable CS0618
            var formats = await clipboard.GetFormatsAsync();
            foreach (var format in formats)
            {
                try 
                {
                    var data = await clipboard.GetDataAsync(format);
                    if (data != null)
                    {
                        backedUpData[format] = data;
                    }
                }
                catch 
                {
                    // Can fail for specific formats or locked clipboard
                }
            }
#pragma warning restore CS0618
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to back up clipboard");
        }
        return backedUpData;
    }

    /// <summary>
    /// Restores the clipboard content from backup.
    /// </summary>
    public static async Task RestoreClipboardAsync(Dictionary<string, object>? backup, ILogger? logger = null)
    {
        if (backup == null || backup.Count == 0) return;

        try 
        {
            var clipboard = GetClipboard();
            if (clipboard == null) return;

#pragma warning disable CS0618
            var dataObject = new DataObject();
            foreach (var kvp in backup)
            {
                dataObject.Set(kvp.Key, kvp.Value);
            }
            await clipboard.SetDataObjectAsync(dataObject);
#pragma warning restore CS0618
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to restore clipboard data");
        }
    }

    private static IClipboard? GetClipboard()
    {
        return (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard;
    }
}
