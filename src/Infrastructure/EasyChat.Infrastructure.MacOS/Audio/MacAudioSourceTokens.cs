using System.Globalization;
using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.MacOS.Audio;

/// <summary>A decoded audio source: what to capture, and which one when there is a choice.</summary>
internal readonly record struct MacAudioSource(AudioCaptureSourceKind Kind, string Identifier)
{
    /// <summary>The process id of the target application, for an application source.</summary>
    internal int ProcessIdentifier =>
        int.TryParse(Identifier, CultureInfo.InvariantCulture, out var value) ? value : 0;
}

/// <summary>
/// Encodes and decodes <see cref="AudioCaptureSourceToken"/> for macOS.
/// </summary>
/// <remarks>
/// The encoding is private to this assembly; Application only ever passes the token back unchanged.
/// Unlike the window target token this one carries no session stamp, because a microphone's device
/// UID is genuinely stable and the picker's selection should survive the device being unplugged and
/// plugged back in. An application source does carry a process id and so is only meaningful while
/// that process lives, which the capture checks at the point of use.
/// </remarks>
internal static class MacAudioSourceTokens
{
    private const string SystemOutput = "macos:system-output";
    private const string ApplicationPrefix = "macos:application:";
    private const string MicrophonePrefix = "macos:microphone:";

    internal static AudioCaptureSourceToken ForSystemOutput() => new(SystemOutput);

    internal static AudioCaptureSourceToken ForApplication(int processIdentifier) =>
        new($"{ApplicationPrefix}{processIdentifier.ToString(CultureInfo.InvariantCulture)}");

    internal static AudioCaptureSourceToken ForMicrophone(string deviceUid) =>
        new($"{MicrophonePrefix}{deviceUid}");

    internal static bool TryDecode(AudioCaptureSourceToken token, out MacAudioSource source)
    {
        source = default;
        if (token.IsEmpty)
            return false;

        var value = token.Value;
        if (string.Equals(value, SystemOutput, StringComparison.Ordinal))
        {
            source = new MacAudioSource(AudioCaptureSourceKind.SystemOutput, string.Empty);
            return true;
        }

        if (value.StartsWith(ApplicationPrefix, StringComparison.Ordinal))
        {
            var identifier = value[ApplicationPrefix.Length..];
            if (!int.TryParse(identifier, CultureInfo.InvariantCulture, out var processIdentifier)
                || processIdentifier <= 0)
            {
                return false;
            }

            source = new MacAudioSource(AudioCaptureSourceKind.Application, identifier);
            return true;
        }

        if (value.StartsWith(MicrophonePrefix, StringComparison.Ordinal))
        {
            var deviceUid = value[MicrophonePrefix.Length..];
            if (string.IsNullOrWhiteSpace(deviceUid))
                return false;

            source = new MacAudioSource(AudioCaptureSourceKind.Microphone, deviceUid);
            return true;
        }

        return false;
    }
}
