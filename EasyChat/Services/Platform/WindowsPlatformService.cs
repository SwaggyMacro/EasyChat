using System;
using System.Runtime.InteropServices;
using EasyChat.Services.Abstractions;

namespace EasyChat.Services.Platform;

public class WindowsPlatformService : IPlatformService
{
    public IntPtr GetForegroundWindowHandle()
    {
        return Win32.GetForegroundWindow();
    }

    public void SetForegroundWindow(IntPtr hWnd)
    {
        Win32.SetForegroundWindow(hWnd);
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

    internal static class Win32
    {
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}