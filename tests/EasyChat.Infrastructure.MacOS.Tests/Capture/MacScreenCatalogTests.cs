using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Capture;
using EasyChat.Infrastructure.MacOS.Input;

namespace EasyChat.Infrastructure.MacOS.Tests.Capture;

[TestClass]
public sealed class MacScreenCatalogTests
{
    private static bool Skip()
    {
        if (OperatingSystem.IsMacOSVersionAtLeast(26))
            return false;

        Assert.Inconclusive("Display geometry can only be verified on macOS 26 or later.");
        return true;
    }

    [TestMethod]
    public async Task TheDesktopHasExactlyOnePrimaryDisplay()
    {
        if (Skip())
            return;

        var screens = await new MacScreenCatalog().GetScreensAsync();

        Assert.IsNotEmpty(screens);
        Assert.AreEqual(1, screens.Count(screen => screen.IsPrimary));
    }

    [TestMethod]
    public async Task EveryDisplayReportsPhysicalPixelsAndAMatchingEffectiveDpi()
    {
        if (Skip())
            return;

        var screens = await new MacScreenCatalog().GetScreensAsync();
        var displays = MacDisplayGeometry.GetDisplays();

        Assert.HasCount(displays.Count, screens);
        foreach (var display in displays)
        {
            var screen = screens.Single(candidate => candidate.Id == display.Id);

            // The bounds are the display's point rectangle scaled by its own backing factor, so on
            // a Retina display they are twice the point size, not equal to it.
            Assert.AreEqual(
                (int)Math.Round(display.PointBounds.Width * display.ScaleX),
                screen.Bounds.Width);
            Assert.AreEqual(
                (int)Math.Round(display.PointBounds.Height * display.ScaleY),
                screen.Bounds.Height);
            Assert.AreEqual(display.ScaleX, screen.ScaleX, 0.001);
            Assert.AreEqual(display.ScaleY, screen.ScaleY, 0.001);
            Assert.AreEqual(ScreenDescriptor.LogicalDpi * display.ScaleX, screen.DpiX, 0.001);
        }
    }

    [TestMethod]
    public async Task DisplayIdentitiesAreStableAndDistinct()
    {
        if (Skip())
            return;

        var catalog = new MacScreenCatalog();
        var first = await catalog.GetScreensAsync();
        var second = await catalog.GetScreensAsync();

        Assert.AreEqual(
            first.Count,
            first.Select(screen => screen.Id).Distinct().Count(),
            "Display identities must not collide.");
        CollectionAssert.AreEqual(
            first.Select(screen => screen.Id).ToArray(),
            second.Select(screen => screen.Id).ToArray());
    }

    [TestMethod]
    public async Task ThePointerIsInsideOneOfTheReportedDisplays()
    {
        if (Skip())
            return;

        var screens = await new MacScreenCatalog().GetScreensAsync();
        var pointer = new MacPointerPosition().GetCurrent();

        Assert.IsTrue(
            screens.Any(screen =>
                pointer.X >= screen.Bounds.X
                && pointer.X <= screen.Bounds.X + screen.Bounds.Width
                && pointer.Y >= screen.Bounds.Y
                && pointer.Y <= screen.Bounds.Y + screen.Bounds.Height),
            $"The pointer at ({pointer.X},{pointer.Y}) fell outside every reported display.");
    }

    [TestMethod]
    public void APointOnTheMainDisplayScalesFromItsOwnOrigin()
    {
        if (Skip())
            return;

        var displays = MacDisplayGeometry.GetDisplays();
        var main = displays.Single(display => display.IsPrimary);

        var origin = MacDisplayGeometry.ToPhysicalPoint(
            new EasyChat.Infrastructure.MacOS.Native.CoreGraphicsPoint
            {
                X = main.PointBounds.X,
                Y = main.PointBounds.Y
            },
            displays);

        Assert.AreEqual(main.PixelBounds.X, origin.X);
        Assert.AreEqual(main.PixelBounds.Y, origin.Y);
    }
}
