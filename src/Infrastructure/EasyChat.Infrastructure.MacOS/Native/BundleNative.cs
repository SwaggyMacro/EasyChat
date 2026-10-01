namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>
/// Reads Info.plist metadata of an application bundle. Must run inside an
/// <see cref="AutoreleasePool"/> opened by the caller.
/// </summary>
internal static class BundleNative
{
    internal static string? ReadInfoString(IntPtr bundleUrl, params string[] keys)
    {
        if (bundleUrl == IntPtr.Zero)
            return null;

        var bundle = ObjectiveCNative.SendReturningHandle(
            ObjectiveCNative.GetClass("NSBundle"),
            ObjectiveCNative.GetSelector("bundleWithURL:"),
            bundleUrl);
        if (bundle == IntPtr.Zero)
            return null;

        foreach (var key in keys)
        {
            var value = FoundationNative.ReadString(ObjectiveCNative.SendReturningHandle(
                bundle,
                ObjectiveCNative.GetSelector("objectForInfoDictionaryKey:"),
                FoundationNative.CreateString(key)));
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
