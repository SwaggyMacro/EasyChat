using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.MacOS.Capture;

/// <summary>
/// Reports the desktop's displays in the contract's unified physical pixel space, with an effective
/// DPI of 96 multiplied by each display's backing scale.
/// </summary>
public sealed class MacScreenCatalog : IScreenCatalog
{
    public ValueTask<IReadOnlyList<ScreenDescriptor>> GetScreensAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
            return ValueTask.FromResult<IReadOnlyList<ScreenDescriptor>>([]);

        var screens = MacDisplayGeometry.GetDisplays()
            .Select(display => new ScreenDescriptor(
                display.Id,
                display.PixelBounds,
                ScreenDescriptor.LogicalDpi * display.ScaleX,
                ScreenDescriptor.LogicalDpi * display.ScaleY,
                display.IsPrimary))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<ScreenDescriptor>>(screens);
    }
}
