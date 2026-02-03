using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using EasyChat.Services.Abstractions;
using Microsoft.Extensions.Logging;

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

            var result = Win32.SetForegroundWindow(hWnd);
            if (!result)
            {
                _logger.LogWarning($"SetForegroundWindow failed for hWnd: {hWnd}");
            }
            
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
                 var down = new Win32.INPUT { type = Win32.INPUT_KEYBOARD, u = new Win32.InputUnion { ki = new Win32.KEYBDINPUT { wVk = 0x0D, dwFlags = 0 } } };
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
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var clipboard = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard;
                if (clipboard == null) return;

                await clipboard.SetTextAsync(text);
            });
            
            await Task.Delay(50);

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to paste text");
        }
    }
    
    public async Task<string?> GetSelectedTextAsync(int? x = null, int? y = null)
    {
        // User requested Clipboard approach ensuring original content is restored.
        // Backup/Restore is handled by the caller (SelectionTranslationService) using ClipboardHelper.
        // This method strictly performs: Clear -> Save modifier state -> Release modifiers -> Copy (Ctrl+C) -> Restore modifiers -> Read.
        
        return await Task.Run(async () =>
        {
            // Track which modifier keys the user is currently pressing
            var pressedModifiers = new List<ushort>();
            
            try
            {
                // 1. Clear Clipboard (to detect new copy)
                // Use OpenClipboard/EmptyClipboard for reliability
                int retryCount = 0;
                bool cleared = false;
                while (retryCount < 5 && !cleared)
                {
                    if (Win32.OpenClipboard(IntPtr.Zero))
                    {
                        Win32.EmptyClipboard();
                        Win32.CloseClipboard();
                        cleared = true;
                    }
                    else
                    {
                        retryCount++;
                        await Task.Delay(10);
                    }
                }
                
                if (!cleared)
                {
                    _logger.LogWarning("Failed to clear clipboard, text extraction might be inaccurate");
                }
                
                // 2. Detect and temporarily release user's modifier keys
                await Task.Delay(10);
                
                // Check which modifier keys are currently pressed by user
                if ((Win32.GetAsyncKeyState(0x11) & 0x8000) != 0) pressedModifiers.Add(0x11); // VK_CONTROL
                if ((Win32.GetAsyncKeyState(0x10) & 0x8000) != 0) pressedModifiers.Add(0x10); // VK_SHIFT  
                if ((Win32.GetAsyncKeyState(0x12) & 0x8000) != 0) pressedModifiers.Add(0x12); // VK_MENU (Alt)
                
                if (pressedModifiers.Count > 0)
                {
                    _logger.LogDebug("User has {Count} modifier keys pressed, temporarily releasing them", pressedModifiers.Count);
                    
                    // Release all pressed modifier keys
                    var releaseInputs = new Win32.INPUT[pressedModifiers.Count];
                    for (int i = 0; i < pressedModifiers.Count; i++)
                    {
                        releaseInputs[i].type = Win32.INPUT_KEYBOARD;
                        releaseInputs[i].u.ki.wVk = pressedModifiers[i];
                        releaseInputs[i].u.ki.dwFlags = Win32.KEYEVENTF_KEYUP;
                    }
                    Win32.SendInput((uint)releaseInputs.Length, releaseInputs, Marshal.SizeOf(typeof(Win32.INPUT)));
                    
                    // Small delay to let the release take effect
                    await Task.Delay(5);
                }
                
                // 3. Send Ctrl+C
                var inputs = new Win32.INPUT[4];
                
                // Ctrl Down
                inputs[0].type = Win32.INPUT_KEYBOARD;
                inputs[0].u.ki.wVk = 0x11; // VK_CONTROL
                
                // C Down
                inputs[1].type = Win32.INPUT_KEYBOARD;
                inputs[1].u.ki.wVk = 0x43; // C
                
                // C Up
                inputs[2].type = Win32.INPUT_KEYBOARD;
                inputs[2].u.ki.wVk = 0x43;
                inputs[2].u.ki.dwFlags = Win32.KEYEVENTF_KEYUP;
                
                // Ctrl Up
                inputs[3].type = Win32.INPUT_KEYBOARD;
                inputs[3].u.ki.wVk = 0x11;
                inputs[3].u.ki.dwFlags = Win32.KEYEVENTF_KEYUP;
                
                Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Win32.INPUT)));
                
                // 4. Poll for text
                string? result = null;
                // Poll more frequently for faster response
                for (int i = 0; i < 20; i++) 
                {
                    await Task.Delay(10); // 10ms * 20 = 200ms max
                    if (IsClipboardTextAvailable())
                    {
                        result = GetClipboardTextWin32();
                        if (!string.IsNullOrEmpty(result)) break;
                    }
                }
                
                if (!string.IsNullOrEmpty(result))
                {
                    _logger.LogDebug("Got text via Clipboard: {Length} chars", result.Length);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get text via Clipboard");
                return null;
            }
            finally
            {
                // 5. Restore user's modifier key states (always, even on exception)
                if (pressedModifiers.Count > 0)
                {
                    _logger.LogDebug("Restoring {Count} modifier keys for user", pressedModifiers.Count);
                    
                    // Re-press the modifier keys that were held by user
                    var restoreInputs = new Win32.INPUT[pressedModifiers.Count];
                    for (int i = 0; i < pressedModifiers.Count; i++)
                    {
                        restoreInputs[i].type = Win32.INPUT_KEYBOARD;
                        restoreInputs[i].u.ki.wVk = pressedModifiers[i];
                        restoreInputs[i].u.ki.dwFlags = 0; // Key down
                    }
                    Win32.SendInput((uint)restoreInputs.Length, restoreInputs, Marshal.SizeOf(typeof(Win32.INPUT)));
                }
            }
        });
    }

    private bool IsClipboardTextAvailable()
    {
        // CF_UNICODETEXT = 13
        return Win32.IsClipboardFormatAvailable(13);
    }
    
    private string? GetClipboardTextWin32()
    {
        if (!Win32.OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            IntPtr hData = Win32.GetClipboardData(13); // CF_UNICODETEXT
            if (hData != IntPtr.Zero)
            {
                IntPtr pData = Win32.GlobalLock(hData);
                if (pData != IntPtr.Zero)
                {
                    try
                    {
                        return Marshal.PtrToStringUni(pData);
                    }
                    finally
                    {
                        Win32.GlobalUnlock(hData);
                    }
                }
            }
        }
        finally
        {
            Win32.CloseClipboard();
        }
        return null;
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

            SetForegroundWindow(hWnd);

            await Task.Delay(50);
        }

        return Win32.GetForegroundWindow() == hWnd;
    }

    public (int X, int Y) GetCursorPosition()
    {
        if (Win32.GetCursorPos(out var point))
        {
            return (point.X, point.Y);
        }
        return (0, 0);
    }

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    internal static class Win32
    {
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetFocus(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        public static extern IntPtr GetFocus();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, out int wParam, out int lParam);

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

        [DllImport("user32.dll")]
        public static extern bool OpenClipboard(IntPtr hWndNewOwner);
        
        [DllImport("user32.dll")]
        public static extern bool EmptyClipboard();

        [DllImport("user32.dll")]
        public static extern bool CloseClipboard();
        
        [DllImport("user32.dll")]
        public static extern IntPtr GetClipboardData(uint uFormat);
        
        [DllImport("user32.dll")]
        public static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalLock(IntPtr hMem);
        
        [DllImport("kernel32.dll")]
        public static extern bool GlobalUnlock(IntPtr hMem);
        
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);
        
        [StructLayout(LayoutKind.Sequential)]
        public struct FORMATETC
        {
            public short cfFormat;
            public IntPtr ptd;
            public int dwAspect;
            public int lindex;
            public int tymed;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct STGMEDIUM
        {
            public int tymed;
            public IntPtr unionmember;
            public IntPtr pUnkForRelease;
        }
    }
}