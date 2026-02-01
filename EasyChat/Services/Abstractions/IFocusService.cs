using System;

namespace EasyChat.Services.Abstractions;

/// <summary>
/// Service for managing window focus behavior.
/// </summary>
public interface IFocusService
{
    /// <summary>
    /// Configures a window to not steal focus when shown or clicked.
    /// </summary>
    /// <param name="windowHandle">The native window handle.</param>
    void SetWindowNoActivate(IntPtr windowHandle);
}
