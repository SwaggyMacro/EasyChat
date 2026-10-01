namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>
/// Converts an application's <c>NSImage</c> icon into PNG bytes, which is the only image form the
/// contract carries. Must run inside an <see cref="AutoreleasePool"/> opened by the caller.
/// </summary>
internal static class AppIconNative
{
    /// <summary>Value of <c>NSBitmapImageFileTypePNG</c>.</summary>
    private const nint PngFileType = 4;

    internal static byte[]? ReadPng(IntPtr icon)
    {
        if (icon == IntPtr.Zero)
            return null;

        var tiff = ObjectiveCNative.SendReturningHandle(
            icon,
            ObjectiveCNative.GetSelector("TIFFRepresentation"));
        if (tiff == IntPtr.Zero)
            return null;

        var representation = ObjectiveCNative.SendReturningHandle(
            ObjectiveCNative.GetClass("NSBitmapImageRep"),
            ObjectiveCNative.GetSelector("imageRepWithData:"),
            tiff);
        if (representation == IntPtr.Zero)
            return null;

        var properties = ObjectiveCNative.SendReturningHandle(
            ObjectiveCNative.GetClass("NSDictionary"),
            ObjectiveCNative.GetSelector("dictionary"));
        var png = ObjectiveCNative.SendReturningHandle(
            representation,
            ObjectiveCNative.GetSelector("representationUsingType:properties:"),
            PngFileType,
            properties);
        return FoundationNative.ReadData(png);
    }
}
