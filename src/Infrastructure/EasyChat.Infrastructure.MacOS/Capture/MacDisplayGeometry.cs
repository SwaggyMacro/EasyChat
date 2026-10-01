using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS.Capture;

/// <summary>One display, in both the units macOS speaks and the units the contract speaks.</summary>
internal sealed record MacDisplay(
    uint DisplayId,
    ScreenId Id,
    CoreGraphicsRect PointBounds,
    PhysicalScreenRegion PixelBounds,
    double ScaleX,
    double ScaleY,
    bool IsPrimary);

/// <summary>
/// Converts between the global display space macOS uses, which is measured in points, and the
/// unified physical pixel space the contract defines.
/// </summary>
/// <remarks>
/// Each display's point rectangle is scaled by that display's own backing factor. On a desktop whose
/// displays share a scale this is exact. On a mixed-scale desktop the resulting pixel rectangles can
/// leave a gap or overlap where two displays meet, because macOS lays displays out in points and no
/// single pixel grid spans them; every operation here therefore resolves a point to its containing
/// display first and converts within that display, so the seam never affects a real capture or a
/// real pointer reading.
/// </remarks>
internal static class MacDisplayGeometry
{
    internal static IReadOnlyList<MacDisplay> GetDisplays()
    {
        var main = DisplayNative.CGMainDisplayID();
        var displays = new List<MacDisplay>();
        foreach (var displayId in DisplayNative.ActiveDisplays())
        {
            var bounds = DisplayNative.CGDisplayBounds(displayId);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                continue;

            var (scaleX, scaleY) = DisplayNative.BackingScale(displayId);
            displays.Add(new MacDisplay(
                displayId,
                Identify(displayId),
                bounds,
                new PhysicalScreenRegion(
                    (int)Math.Round(bounds.X * scaleX),
                    (int)Math.Round(bounds.Y * scaleY),
                    (int)Math.Round(bounds.Width * scaleX),
                    (int)Math.Round(bounds.Height * scaleY)),
                scaleX,
                scaleY,
                displayId == main));
        }

        return displays;
    }

    /// <summary>
    /// Converts a point in the global display space to unified physical pixels, using the scale of
    /// the display that contains it. A point outside every display — which macOS can report while a
    /// display is being disconnected — falls back to the main display's scale.
    /// </summary>
    internal static PhysicalScreenPoint ToPhysicalPoint(
        CoreGraphicsPoint point,
        IReadOnlyList<MacDisplay> displays)
    {
        var display = Containing(point, displays) ?? displays.FirstOrDefault(item => item.IsPrimary);
        if (display is null)
            return new PhysicalScreenPoint((int)Math.Round(point.X), (int)Math.Round(point.Y));

        var offsetX = point.X - display.PointBounds.X;
        var offsetY = point.Y - display.PointBounds.Y;
        return new PhysicalScreenPoint(
            display.PixelBounds.X + (int)Math.Round(offsetX * display.ScaleX),
            display.PixelBounds.Y + (int)Math.Round(offsetY * display.ScaleY));
    }

    private static MacDisplay? Containing(
        CoreGraphicsPoint point,
        IReadOnlyList<MacDisplay> displays) =>
        displays.FirstOrDefault(display =>
            point.X >= display.PointBounds.X
            && point.X < display.PointBounds.X + display.PointBounds.Width
            && point.Y >= display.PointBounds.Y
            && point.Y < display.PointBounds.Y + display.PointBounds.Height);

    /// <summary>
    /// A display identity that survives a reboot and a reconnect, unlike the
    /// <c>CGDirectDisplayID</c>, which macOS reassigns.
    /// </summary>
    private static ScreenId Identify(uint displayId) => new(string.Join(
        '-',
        DisplayNative.CGDisplayVendorNumber(displayId),
        DisplayNative.CGDisplayModelNumber(displayId),
        DisplayNative.CGDisplaySerialNumber(displayId),
        DisplayNative.CGDisplayUnitNumber(displayId)));
}
