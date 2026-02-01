using System;
using System.Runtime.InteropServices;
using EasyChat.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace EasyChat.Services.Platform;

/// <summary>
/// Windows implementation of focus management service.
/// </summary>
public class WindowsFocusService : IFocusService
{
    private readonly ILogger<WindowsFocusService> _logger;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public WindowsFocusService(ILogger<WindowsFocusService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void SetWindowNoActivate(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            _logger.LogWarning("SetWindowNoActivate called with null handle");
            return;
        }

        try
        {
            var exStyle = GetWindowLong(windowHandle, GWL_EXSTYLE);
            SetWindowLong(windowHandle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);
            _logger.LogDebug("Applied WS_EX_NOACTIVATE to window handle: {Handle}", windowHandle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set WS_EX_NOACTIVATE on window");
        }
    }
}
