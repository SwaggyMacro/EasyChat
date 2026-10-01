using System.Buffers.Binary;
using System.Text;

namespace EasyChat.Infrastructure.MacOS.Audio;

/// <summary>
/// Builds small uncompressed WAV files in memory.
/// </summary>
/// <remarks>
/// Used for the interpretation cues. The Windows adapter produces them with <c>Console.Beep</c>,
/// which does not exist off Windows, so the same distinct pitches are synthesised here and played
/// through the ordinary playback path. That keeps the cues audibly identical across platforms
/// instead of substituting whatever system alert sound happens to be to hand.
/// </remarks>
internal static class WaveAudio
{
    private const int SampleRateHz = 44_100;
    private const int BitsPerSample = 16;
    private const int ChannelCount = 1;

    /// <summary>Value of the WAV format tag for uncompressed PCM.</summary>
    private const ushort PcmFormat = 1;

    /// <summary>A sine tone, faded at both ends so it does not click.</summary>
    internal static byte[] CreateTone(int frequencyHz, int durationMilliseconds, double amplitude = 0.25)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frequencyHz, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(durationMilliseconds, 1);

        var sampleCount = SampleRateHz * durationMilliseconds / 1000;
        var samples = new short[sampleCount];
        var fade = Math.Min(sampleCount / 10, SampleRateHz / 200);
        for (var index = 0; index < sampleCount; index++)
        {
            var envelope = 1d;
            if (fade > 0 && index < fade)
                envelope = (double)index / fade;
            else if (fade > 0 && index >= sampleCount - fade)
                envelope = (double)(sampleCount - index) / fade;

            var value = Math.Sin(2 * Math.PI * frequencyHz * index / SampleRateHz)
                        * amplitude
                        * envelope;
            samples[index] = (short)Math.Clamp(value * short.MaxValue, short.MinValue, short.MaxValue);
        }

        return CreatePcm16(samples);
    }

    /// <summary>Wraps 16-bit mono samples in a WAV container.</summary>
    internal static byte[] CreatePcm16(ReadOnlySpan<short> samples)
    {
        const int headerLength = 44;
        var dataLength = samples.Length * sizeof(short);
        var file = new byte[headerLength + dataLength];
        var span = file.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataLength);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(span[20..], PcmFormat);
        BinaryPrimitives.WriteUInt16LittleEndian(span[22..], ChannelCount);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRateHz);
        BinaryPrimitives.WriteInt32LittleEndian(
            span[28..],
            SampleRateHz * ChannelCount * (BitsPerSample / 8));
        BinaryPrimitives.WriteUInt16LittleEndian(span[32..], ChannelCount * (BitsPerSample / 8));
        BinaryPrimitives.WriteUInt16LittleEndian(span[34..], BitsPerSample);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataLength);

        for (var index = 0; index < samples.Length; index++)
            BinaryPrimitives.WriteInt16LittleEndian(span[(headerLength + (index * 2))..], samples[index]);

        return file;
    }

    /// <summary>
    /// The file extension AVFoundation needs in order to pick a decoder, derived from the media type
    /// the speech provider reported.
    /// </summary>
    internal static string ExtensionFor(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        "audio/mpeg" or "audio/mp3" => ".mp3",
        "audio/wav" or "audio/wave" or "audio/x-wav" => ".wav",
        "audio/mp4" or "audio/aac" => ".m4a",
        "audio/ogg" => ".ogg",
        "audio/flac" => ".flac",
        // Edge TTS answers MPEG audio, and an unknown type is far more likely to be that than
        // anything else EasyChat receives.
        _ => ".mp3"
    };
}
