using System;
using System.Threading.Tasks;

namespace EasyChat.Services.Abstractions;

public interface IPlatformService
{
    IntPtr GetForegroundWindowHandle();
    void SetForegroundWindow(IntPtr hWnd);
    void SetFocus(IntPtr hWnd);
    void PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    void SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    Task SendTextAsync(string text, int delayMs = 10);
    Task PasteTextAsync(string text);
    Task SendTextMessageAsync(IntPtr hWnd, string text, int delayMs = 10);
    Task<bool> EnsureFocused(IntPtr hWnd);
}