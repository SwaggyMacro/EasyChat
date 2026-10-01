using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>Values of <c>NSApplicationActivationPolicy</c>.</summary>
internal enum ActivationPolicy : long
{
    /// <summary>An ordinary application with a Dock icon and a user interface.</summary>
    Regular = 0,
    Accessory = 1,
    Prohibited = 2
}

/// <summary>
/// <c>NSWorkspace</c> and <c>NSRunningApplication</c>. Every call must run inside an
/// <see cref="AutoreleasePool"/> opened by the caller.
/// </summary>
internal static class WorkspaceNative
{
    private const string LibraryPath = "/System/Library/Frameworks/AppKit.framework/AppKit";

    private static readonly Lazy<IntPtr> Shared = new(
        LoadSharedWorkspace,
        LazyThreadSafetyMode.ExecutionAndPublication);

    internal static IntPtr FrontmostApplication() =>
        ObjectiveCNative.SendReturningHandle(
            Shared.Value,
            ObjectiveCNative.GetSelector("frontmostApplication"));

    internal static IntPtr RunningApplications() =>
        ObjectiveCNative.SendReturningHandle(
            Shared.Value,
            ObjectiveCNative.GetSelector("runningApplications"));

    internal static IntPtr ApplicationWithProcessIdentifier(int processIdentifier)
    {
        EnsureLoaded();
        return ObjectiveCNative.SendReturningHandle(
            ObjectiveCNative.GetClass("NSRunningApplication"),
            ObjectiveCNative.GetSelector("runningApplicationWithProcessIdentifier:"),
            processIdentifier);
    }

    internal static int ProcessIdentifier(IntPtr application) =>
        application == IntPtr.Zero
            ? 0
            : ObjectiveCNative.SendReturningInt32(
                application,
                ObjectiveCNative.GetSelector("processIdentifier"));

    internal static string? BundleIdentifier(IntPtr application) =>
        FoundationNative.ReadString(ObjectiveCNative.SendReturningHandle(
            application,
            ObjectiveCNative.GetSelector("bundleIdentifier")));

    internal static string? LocalizedName(IntPtr application) =>
        FoundationNative.ReadString(ObjectiveCNative.SendReturningHandle(
            application,
            ObjectiveCNative.GetSelector("localizedName")));

    internal static IntPtr Icon(IntPtr application) =>
        ObjectiveCNative.SendReturningHandle(
            application,
            ObjectiveCNative.GetSelector("icon"));

    internal static IntPtr BundleUrl(IntPtr application) =>
        ObjectiveCNative.SendReturningHandle(
            application,
            ObjectiveCNative.GetSelector("bundleURL"));

    internal static ActivationPolicy Policy(IntPtr application) =>
        (ActivationPolicy)ObjectiveCNative.SendReturningNInt(
            application,
            ObjectiveCNative.GetSelector("activationPolicy"));

    internal static bool IsTerminated(IntPtr application) =>
        application == IntPtr.Zero
        || ObjectiveCNative.SendReturningBool(
            application,
            ObjectiveCNative.GetSelector("isTerminated"),
            IntPtr.Zero);

    /// <summary>
    /// Brings an application forward. <c>NSApplicationActivateAllWindows</c> is passed so the
    /// application's document windows come with it, matching what a Dock click does.
    /// </summary>
    internal static bool Activate(IntPtr application)
    {
        const nint activateAllWindows = 1;
        return ObjectiveCNative.SendReturningBool(
            application,
            ObjectiveCNative.GetSelector("activateWithOptions:"),
            activateAllWindows);
    }

    private static void EnsureLoaded()
    {
        if (Shared.Value == IntPtr.Zero)
            throw new PlatformNotSupportedException("NSWorkspace is unavailable.");
    }

    private static IntPtr LoadSharedWorkspace()
    {
        NativeLibrary.Load(LibraryPath);
        using var pool = AutoreleasePool.Push();
        var workspace = ObjectiveCNative.SendReturningHandle(
            ObjectiveCNative.GetClass("NSWorkspace"),
            ObjectiveCNative.GetSelector("sharedWorkspace"));
        if (workspace == IntPtr.Zero)
            throw new PlatformNotSupportedException("NSWorkspace is unavailable.");

        return ObjectiveCNative.SendReturningHandle(
            workspace,
            ObjectiveCNative.GetSelector("retain"));
    }
}
