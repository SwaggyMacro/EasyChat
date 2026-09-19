using System.Globalization;
using System.Runtime.Versioning;
using EasyChat.Contracts.Platform;
using SoundFlow.Backends.MiniAudio;

namespace EasyChat.Infrastructure.Windows.Audio;

[SupportedOSPlatform("windows")]
public sealed class WindowsAudioPlaybackDeviceCatalog : IAudioPlaybackDeviceCatalog
{
    private const string DeviceTokenPrefix = "windows:playback:";

    public ValueTask<IReadOnlyList<AudioPlaybackDeviceDescriptor>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var engine = new MiniAudioEngine();
        engine.UpdateAudioDevicesInfo();
        var devices = engine.PlaybackDevices
            .Select(device => new AudioPlaybackDeviceDescriptor(
                FromDeviceId(
                    device.Id.ToInt64().ToString(CultureInfo.InvariantCulture),
                    device.Name),
                device.Name,
                device.Name,
                device.IsDefault ? "Default playback device" : null,
                IsVirtualCableName(device.Name)))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<AudioPlaybackDeviceDescriptor>>(devices);
    }

    internal static AudioPlaybackDeviceToken FromDeviceId(
        string deviceId,
        string? deviceName = null) =>
        new($"{DeviceTokenPrefix}{deviceId}", deviceName);

    internal static bool IsVirtualCableName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase)
        && (name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)
            || name.Contains("CABLE In", StringComparison.OrdinalIgnoreCase));
}
