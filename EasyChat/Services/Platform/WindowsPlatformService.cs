using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using EasyChat.Services.Abstractions;
using Microsoft.Extensions.Logging;
// ReSharper disable InconsistentNaming

namespace EasyChat.Services.Platform;

public class WindowsPlatformService : IPlatformService
{
    private readonly ILogger<WindowsPlatformService> _logger;

    public WindowsPlatformService(ILogger<WindowsPlatformService> logger)
    {
        _logger = logger;
    }

    public IntPtr GetForegroundWindowHandle()
    {
        return Win32.GetForegroundWindow();
    }

    public void SetForegroundWindow(IntPtr hWnd)
    {
        // Robustly set foreground window using AttachThreadInput hack
        // This is necessary because Windows restricts processes from stealing focus
        // unless they are complying with specific rules.
        
        var targetThreadId = Win32.GetWindowThreadProcessId(hWnd, out _);
        var currentThreadId = Win32.GetCurrentThreadId();
        var attached = false;

        try 
        {
            if (currentThreadId != targetThreadId)
            {
                attached = Win32.AttachThreadInput(currentThreadId, targetThreadId, true);
                if (!attached)
                {
                    _logger.LogWarning($"AttachThreadInput failed using ids: {currentThreadId} -> {targetThreadId}");
                }
            }

            // Retry a few times if needed, or just call it.
            var result = Win32.SetForegroundWindow(hWnd);
            if (!result)
            {
                _logger.LogWarning($"SetForegroundWindow failed for hWnd: {hWnd}");
            }
            
            // Try SetFocus as well, now that we are attached or if we own it
            Win32.SetFocus(hWnd);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SetForegroundWindow");
        }
        finally
        {
            if (attached)
            {
                Win32.AttachThreadInput(currentThreadId, targetThreadId, false);
            }
        }
    }

    public void SetFocus(IntPtr hWnd)
    {
        Win32.SetFocus(hWnd);
    }

    public void PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        Win32.PostMessage(hWnd, msg, wParam, lParam);
    }

    public void SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        Win32.SendMessage(hWnd, msg, wParam, lParam);
    }

    public async Task SendTextAsync(string text, int delayMs = 10)
    {
        if (string.IsNullOrEmpty(text)) return;

        foreach (var c in text)
        {
            if (c == '\r') continue; 

            var inputs = new List<Win32.INPUT>();

            if (c == '\n')
            {
                // Send VK_RETURN for newline
                 var down = new Win32.INPUT { type = Win32.INPUT_KEYBOARD, u = new Win32.InputUnion { ki = new Win32.KEYBDINPUT { wVk = 0x0D, dwFlags = 0 } } }; // VK_RETURN
                 var up = new Win32.INPUT { type = Win32.INPUT_KEYBOARD, u = new Win32.InputUnion { ki = new Win32.KEYBDINPUT { wVk = 0x0D, dwFlags = Win32.KEYEVENTF_KEYUP } } };
                 inputs.Add(down);
                 inputs.Add(up);
            }
            else
            {
                var down = new Win32.INPUT
                {
                    type = Win32.INPUT_KEYBOARD,
                    u = new Win32.InputUnion
                    {
                        ki = new Win32.KEYBDINPUT
                        {
                            wScan = c,
                            dwFlags = Win32.KEYEVENTF_UNICODE
                        }
                    }
                };

                var up = new Win32.INPUT
                {
                    type = Win32.INPUT_KEYBOARD,
                    u = new Win32.InputUnion
                    {
                        ki = new Win32.KEYBDINPUT
                        {
                            wScan = c,
                            dwFlags = Win32.KEYEVENTF_UNICODE | Win32.KEYEVENTF_KEYUP
                        }
                    }
                };

                inputs.Add(down);
                inputs.Add(up);
            }

            Win32.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(Win32.INPUT)));
            
            await Task.Delay(delayMs); 
        }
        
        await Task.CompletedTask;
    }

    [Obsolete("Obsolete")]
    public async Task PasteTextAsync(string text)
    {
        try
        {
            var clipboard = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard;
            if (clipboard == null) return;

            // 1. Backup all clipboard data
            var backedUpData = new Dictionary<string, object>();
            try
            {
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
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to inspect clipboard formats for backup");
            }

            // 2. Set new text check
            await clipboard.SetTextAsync(text);
            
            // Wait slightly for clipboard
            await Task.Delay(50);

            // 3. Send Ctrl+V
            // VK_CONTROL = 0x11, V = 0x56
            var inputs = new List<Win32.INPUT>();
            
            var ctrlDown = new Win32.INPUT { type = Win32.INPUT_KEYBOARD, u = new Win32.InputUnion { ki = new Win32.KEYBDINPUT { wVk = 0x11, dwFlags = 0 } } };
            var vDown = new Win32.INPUT { type = Win32.INPUT_KEYBOARD, u = new Win32.InputUnion { ki = new Win32.KEYBDINPUT { wVk = 0x56, dwFlags = 0 } } };
            var vUp = new Win32.INPUT { type = Win32.INPUT_KEYBOARD, u = new Win32.InputUnion { ki = new Win32.KEYBDINPUT { wVk = 0x56, dwFlags = Win32.KEYEVENTF_KEYUP } } };
            var ctrlUp = new Win32.INPUT { type = Win32.INPUT_KEYBOARD, u = new Win32.InputUnion { ki = new Win32.KEYBDINPUT { wVk = 0x11, dwFlags = Win32.KEYEVENTF_KEYUP } } };

            inputs.Add(ctrlDown);
            inputs.Add(vDown);
            inputs.Add(vUp);
            inputs.Add(ctrlUp);

            Win32.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(Win32.INPUT)));
            
            // Wait for app to process paste command before restoring
            await Task.Delay(200);

            // 4. Restore old data
            if (backedUpData.Count > 0)
            {
                try 
                {
                    var dataObject = new Avalonia.Input.DataObject();
                    foreach (var kvp in backedUpData)
                    {
                        dataObject.Set(kvp.Key, kvp.Value);
                    }
                    await clipboard.SetDataObjectAsync(dataObject);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to restore clipboard data");
                }
            }
            else
            {
                // Nothing to restore, just clear our temp text to be safe/clean
                await clipboard.ClearAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to paste text");
        }
    }

    public async Task SendTextMessageAsync(IntPtr hWnd, string text, int delayMs = 10)
    {
        foreach (var c in text)
        {
            Win32.PostMessage(hWnd, Constants.Windows.WM_CHAR, c, IntPtr.Zero);
            await Task.Delay(delayMs);
        }
    }

    public async Task<bool> EnsureFocused(IntPtr hWnd)
    {
        for (int i = 0; i < 5; i++)
        {
            var foreground = Win32.GetForegroundWindow();
            if (foreground == hWnd) return true;

            // Try to set it
            SetForegroundWindow(hWnd);

            // Small wait
            await Task.Delay(50);
        }

        return Win32.GetForegroundWindow() == hWnd;
    }

    internal static class Win32
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        public const int INPUT_MOUSE = 0;
        public const int INPUT_KEYBOARD = 1;
        public const int INPUT_HARDWARE = 2;

        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_UNICODE = 0x0004;
        public const uint KEYEVENTF_SCANCODE = 0x0008;
    }
}