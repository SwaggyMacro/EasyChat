using System;

namespace EasyChat.Services.Abstractions;

public interface IPlatformService
{
    IntPtr GetForegroundWindowHandle();
    void SetForegroundWindow(IntPtr hWnd);
    void SetFocus(IntPtr hWnd);
    void PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    void SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}