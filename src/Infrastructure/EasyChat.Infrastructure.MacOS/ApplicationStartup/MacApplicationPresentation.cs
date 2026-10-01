using System.Runtime.InteropServices;
using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS.ApplicationStartup;

/// <summary>
/// Controls how this process presents itself to the user as an application.
/// </summary>
public static class MacApplicationPresentation
{
    /// <summary>Value of <c>NSApplicationActivationPolicyAccessory</c>.</summary>
    private const nint AccessoryPolicy = 1;

    /// <summary>
    /// Removes this process from the Dock and the application switcher while leaving it able to
    /// show windows and receive events.
    /// </summary>
    /// <remarks>
    /// A helper process launched from the same bundle would otherwise appear as a second EasyChat
    /// to the user, because the activation policy comes from the shared Info.plist and can only be
    /// narrowed at runtime.
    /// </remarks>
    public static bool TryHideFromDock()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
            return false;

        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        using var pool = AutoreleasePool.Push();
        var application = ObjectiveCNative.SendReturningHandle(
            ObjectiveCNative.GetClass("NSApplication"),
            ObjectiveCNative.GetSelector("sharedApplication"));
        if (application == IntPtr.Zero)
            return false;

        return ObjectiveCNative.SendReturningBool(
            application,
            ObjectiveCNative.GetSelector("setActivationPolicy:"),
            AccessoryPolicy);
    }
}
