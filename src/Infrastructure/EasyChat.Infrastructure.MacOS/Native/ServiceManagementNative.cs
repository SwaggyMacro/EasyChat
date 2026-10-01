using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>Value of <c>SMAppServiceStatus</c>.</summary>
internal enum LoginItemStatus : long
{
    NotRegistered = 0,
    Enabled = 1,
    RequiresApproval = 2,
    NotFound = 3
}

/// <summary>
/// Login item registration through <c>SMAppService.mainApp</c>. macOS owns the launch entry, so no
/// <c>LaunchAgents</c> plist is written and no helper process is installed.
/// </summary>
internal static class ServiceManagementNative
{
    private const string LibraryPath =
        "/System/Library/Frameworks/ServiceManagement.framework/ServiceManagement";

    private static readonly Lazy<IntPtr> MainAppService = new(
        LoadMainAppService,
        LazyThreadSafetyMode.ExecutionAndPublication);

    internal static LoginItemStatus ReadStatus() =>
        (LoginItemStatus)ObjectiveCNative.SendReturningNInt(
            MainAppService.Value,
            ObjectiveCNative.GetSelector("status"));

    internal static bool Register(out string? error) =>
        Invoke("registerAndReturnError:", out error);

    internal static bool Unregister(out string? error) =>
        Invoke("unregisterAndReturnError:", out error);

    private static unsafe bool Invoke(string selector, out string? error)
    {
        var failure = IntPtr.Zero;
        var succeeded = ObjectiveCNative.SendReturningBool(
            MainAppService.Value,
            ObjectiveCNative.GetSelector(selector),
            (IntPtr)(&failure));

        error = succeeded || failure == IntPtr.Zero
            ? null
            : ObjectiveCNative.ReadString(ObjectiveCNative.SendReturningHandle(
                failure,
                ObjectiveCNative.GetSelector("localizedDescription")));
        return succeeded;
    }

    private static IntPtr LoadMainAppService()
    {
        // The framework has to be resident before the Objective-C runtime knows the class.
        NativeLibrary.Load(LibraryPath);
        var service = ObjectiveCNative.SendReturningHandle(
            ObjectiveCNative.GetClass("SMAppService"),
            ObjectiveCNative.GetSelector("mainAppService"));
        if (service == IntPtr.Zero)
        {
            throw new PlatformNotSupportedException(
                "SMAppService.mainApp is unavailable on this host.");
        }

        // The class method hands back an autoreleased instance and this process installs no
        // autorelease pool, so the singleton is retained for the lifetime of the process.
        return ObjectiveCNative.SendReturningHandle(service, ObjectiveCNative.GetSelector("retain"));
    }
}
