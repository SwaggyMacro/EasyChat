using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS.Audio;

/// <summary>
/// Lists the devices EasyChat can play through, marking the ones that loop audio back to an input.
/// </summary>
/// <remarks>
/// Which device is virtual is decided here, not in the interface: the contract only carries the
/// <c>IsVirtualCable</c> flag, and hard-coding driver names into the presentation layer would put
/// platform knowledge where it cannot be maintained.
/// </remarks>
internal sealed class MacAudioPlaybackDeviceCatalog : IAudioPlaybackDeviceCatalog
{
    public ValueTask<IReadOnlyList<AudioPlaybackDeviceDescriptor>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
            return ValueTask.FromResult<IReadOnlyList<AudioPlaybackDeviceDescriptor>>([]);

        var defaultUid = CoreAudioNative.DefaultOutputDeviceUid();
        var devices = CoreAudioNative.OutputDevices()
            .Select(device => new AudioPlaybackDeviceDescriptor(
                new AudioPlaybackDeviceToken(device.Uid),
                device.Name,
                string.Equals(device.Uid, defaultUid, StringComparison.Ordinal)
                    ? $"{device.Name} (Default)"
                    : device.Name,
                $"{device.ChannelCount} output channel(s)",
                MacVirtualAudioDevices.IsLoopback(device.Name)))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<AudioPlaybackDeviceDescriptor>>(devices);
    }
}

/// <summary>
/// Recognises the loopback drivers people use on macOS to route audio between applications.
/// </summary>
/// <remarks>
/// This is a name match, and knowingly so: CoreAudio does not flag a device as virtual, because to
/// the system a loopback driver is an ordinary device with both input and output channels. Matching
/// the well-known driver names is the same approach the Windows adapter takes for VB-Audio, applied
/// to the macOS equivalents.
/// </remarks>
internal static class MacVirtualAudioDevices
{
    private static readonly string[] Names =
    [
        "blackhole",
        "loopback",
        "soundflower",
        "vb-cable",
        "existential audio",
        "virtual"
    ];

    internal static bool IsLoopback(string? deviceName) =>
        !string.IsNullOrWhiteSpace(deviceName)
        && Names.Any(name => deviceName.Contains(name, StringComparison.OrdinalIgnoreCase));
}
