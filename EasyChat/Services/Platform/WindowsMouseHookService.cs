using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using EasyChat.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace EasyChat.Services.Platform;

[SuppressMessage("ReSharper", "InconsistentNaming")] 
public class WindowsMouseHookService : IMouseHookService, IDisposable
{
    private readonly ILogger<WindowsMouseHookService> _logger;
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDOWN = 0x0201;

    private LowLevelMouseProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    public event EventHandler<SimpleMouseEventArgs>? MouseUp;
    public event EventHandler<SimpleMouseEventArgs>? MouseDown;

    public WindowsMouseHookService(ILogger<WindowsMouseHookService> logger)
    {
        _logger = logger;
        _proc = HookCallback; // Keep reference to avoid GC
    }

    public void Start()
    {
        if (_hookId == IntPtr.Zero)
        {
            _hookId = SetHook(_proc);
            if (_hookId == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError("Failed to install Mouse Hook. Win32 Error: {Error}", error);
            }
            else
            {
                _logger.LogInformation("Mouse Hook Installed. Handle: {Handle}", _hookId);
            }
        }
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _logger.LogInformation("Mouse Hook Removed");
        }
    }

    private IntPtr SetHook(LowLevelMouseProc proc)
    {
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        
        return SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(curModule?.ModuleName), 0);
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            if (wParam == WM_LBUTTONUP)
            {
                MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                SafeInvoke(MouseUp, hookStruct.pt.x, hookStruct.pt.y);
            }
            else if (wParam == WM_LBUTTONDOWN)
            {
                MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                SafeInvoke(MouseDown, hookStruct.pt.x, hookStruct.pt.y);
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void SafeInvoke(EventHandler<SimpleMouseEventArgs>? handler, int x, int y)
    {
        try 
        {
            handler?.Invoke(this, new SimpleMouseEventArgs(x, y));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Mouse event");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    public void Dispose()
    {
        Stop();
    }
}
