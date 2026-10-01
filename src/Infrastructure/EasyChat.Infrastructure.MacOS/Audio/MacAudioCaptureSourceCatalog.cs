using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS.Audio;

/// <summary>
/// Lists what EasyChat can listen to: the system output, each application that could be producing
/// sound, and every recording device.
/// </summary>
/// <remarks>
/// Nothing here needs a privacy approval. Device enumeration is open to any process and the
/// application list comes from the same workspace query the selection picker uses, so the source
/// picker can be populated before EasyChat asks the user for anything.
/// </remarks>
internal sealed class MacAudioCaptureSourceCatalog : IAudioCaptureSourceCatalog
{
    /// <summary>
    /// Loopback drivers route output back in as input. They matter because interpretation routes
    /// synthesised speech through one, and because capturing from one while playing into it would
    /// feed EasyChat its own voice.
    /// </summary>
    private static readonly string[] LoopbackDriverNames =
        ["blackhole", "loopback", "soundflower", "virtual", "aggregate", "vb-cable"];

    public ValueTask<IReadOnlyList<AudioCaptureSourceDescriptor>> GetSourcesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
            return ValueTask.FromResult<IReadOnlyList<AudioCaptureSourceDescriptor>>([]);

        var sources = new List<AudioCaptureSourceDescriptor>
        {
            new(
                MacAudioSourceTokens.ForSystemOutput(),
                AudioCaptureSourceKind.SystemOutput,
                "System Audio",
                "System Audio",
                "Everything playing through the current output device.",
                ReadOnlyMemory<byte>.Empty,
                IsDefault: true)
        };

        sources.AddRange(ApplicationSources(cancellationToken));
        sources.AddRange(MicrophoneSources());
        return ValueTask.FromResult<IReadOnlyList<AudioCaptureSourceDescriptor>>(sources);
    }

    private static IEnumerable<AudioCaptureSourceDescriptor> ApplicationSources(
        CancellationToken cancellationToken)
    {
        using var pool = AutoreleasePool.Push();
        var applications = WorkspaceNative.RunningApplications();
        var count = FoundationNative.CountOf(applications);
        var self = Environment.ProcessId;
        var sources = new List<AudioCaptureSourceDescriptor>((int)count);
        for (nint index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var application = FoundationNative.ItemAt(applications, index);
            if (WorkspaceNative.Policy(application) != ActivationPolicy.Regular)
                continue;

            var processIdentifier = WorkspaceNative.ProcessIdentifier(application);
            // Capturing EasyChat's own output would feed its subtitles back into recognition.
            if (processIdentifier <= 0 || processIdentifier == self)
                continue;

            var name = WorkspaceNative.LocalizedName(application)
                       ?? WorkspaceNative.BundleIdentifier(application);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            sources.Add(new AudioCaptureSourceDescriptor(
                MacAudioSourceTokens.ForApplication(processIdentifier),
                AudioCaptureSourceKind.Application,
                name,
                name,
                WorkspaceNative.BundleIdentifier(application),
                AppIconNative.ReadPng(WorkspaceNative.Icon(application)) ?? ReadOnlyMemory<byte>.Empty));
        }

        return sources;
    }

    private static IEnumerable<AudioCaptureSourceDescriptor> MicrophoneSources()
    {
        var defaultUid = CoreAudioNative.DefaultInputDeviceUid();
        foreach (var device in CoreAudioNative.InputDevices())
        {
            yield return new AudioCaptureSourceDescriptor(
                MacAudioSourceTokens.ForMicrophone(device.Uid),
                AudioCaptureSourceKind.Microphone,
                device.Name,
                device.Name,
                $"{device.ChannelCount} input channel(s)",
                ReadOnlyMemory<byte>.Empty,
                IsVirtualCable: IsLoopbackDriver(device.Name),
                IsDefault: string.Equals(device.Uid, defaultUid, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// A name match, because CoreAudio does not mark a device as virtual: a loopback driver is an
    /// ordinary input device as far as the system is concerned.
    /// </summary>
    private static bool IsLoopbackDriver(string name) =>
        LoopbackDriverNames.Any(candidate =>
            name.Contains(candidate, StringComparison.OrdinalIgnoreCase));
}
