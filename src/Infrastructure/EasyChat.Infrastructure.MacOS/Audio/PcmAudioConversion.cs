using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.MacOS.Audio;

/// <summary>
/// Turns whatever a macOS device hands over into the single PCM shape speech recognition expects,
/// and sums several sources into one stream.
/// </summary>
/// <remarks>
/// This is deliberately plain managed code with no native dependency: sample-rate conversion,
/// channel downmix and frame alignment are where an audio path quietly goes wrong — half-speed
/// playback, one channel dropped, frames drifting out of alignment — and none of those are visible
/// in a spectrogram glance, only in degraded recognition. Keeping them here makes them testable
/// without a microphone.
/// </remarks>
internal static class PcmAudioConversion
{
    /// <summary>
    /// Converts interleaved 32-bit float samples, the format CoreAudio and ScreenCaptureKit both
    /// deliver, into the requested PCM shape.
    /// </summary>
    internal static byte[] FromFloat32(
        ReadOnlySpan<float> samples,
        int sourceSampleRateHz,
        int sourceChannelCount,
        PcmAudioFormat target)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceSampleRateHz, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceChannelCount, 1);
        if (target.BitsPerSample != 16 || target.ChannelCount != 1)
        {
            throw new NotSupportedException(
                "Only 16-bit mono output is supported, which is what speech recognition consumes.");
        }

        var frameCount = samples.Length / sourceChannelCount;
        if (frameCount == 0)
            return [];

        var mono = Downmix(samples, frameCount, sourceChannelCount);
        var resampled = Resample(mono, sourceSampleRateHz, target.SampleRateHz);
        return ToPcm16(resampled);
    }

    /// <summary>Averages the channels, which keeps loudness steady instead of doubling it.</summary>
    private static float[] Downmix(ReadOnlySpan<float> samples, int frameCount, int channelCount)
    {
        var mono = new float[frameCount];
        if (channelCount == 1)
        {
            samples[..frameCount].CopyTo(mono);
            return mono;
        }

        for (var frame = 0; frame < frameCount; frame++)
        {
            var sum = 0f;
            var offset = frame * channelCount;
            for (var channel = 0; channel < channelCount; channel++)
                sum += samples[offset + channel];
            mono[frame] = sum / channelCount;
        }

        return mono;
    }

    /// <summary>
    /// Linear interpolation between neighbouring samples. Speech recognition consumes 16 kHz, well
    /// under every device rate in practice, so this is always downsampling and the audible cost of a
    /// better kernel would not change what the recogniser hears.
    /// </summary>
    private static float[] Resample(float[] samples, int sourceRate, int targetRate)
    {
        if (sourceRate == targetRate || samples.Length == 0)
            return samples;

        var ratio = (double)targetRate / sourceRate;
        var outputLength = (int)(samples.Length * ratio);
        if (outputLength <= 0)
            return [];

        var output = new float[outputLength];
        for (var index = 0; index < outputLength; index++)
        {
            var position = index / ratio;
            var left = (int)position;
            var right = Math.Min(left + 1, samples.Length - 1);
            var weight = (float)(position - left);
            output[index] = (samples[left] * (1 - weight)) + (samples[right] * weight);
        }

        return output;
    }

    private static byte[] ToPcm16(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var index = 0; index < samples.Length; index++)
        {
            var value = (int)Math.Round(Math.Clamp(samples[index], -1f, 1f) * short.MaxValue);
            var sample = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
            bytes[index * 2] = (byte)(sample & 0xFF);
            bytes[(index * 2) + 1] = (byte)((sample >> 8) & 0xFF);
        }

        return bytes;
    }

    /// <summary>
    /// Sums several 16-bit mono streams. Summing is what makes several speakers audible at once,
    /// and clamping rather than wrapping is what stops a loud moment turning into a burst of noise.
    /// Shorter inputs simply contribute silence for the frames they do not cover.
    /// </summary>
    internal static byte[] Mix(IReadOnlyList<byte[]> streams)
    {
        ArgumentNullException.ThrowIfNull(streams);
        if (streams.Count == 0)
            return [];
        if (streams.Count == 1)
            return streams[0];

        var length = streams.Max(stream => stream.Length) & ~1;
        var mixed = new byte[length];
        for (var offset = 0; offset < length; offset += 2)
        {
            var sum = 0;
            foreach (var stream in streams)
            {
                if (offset + 1 < stream.Length)
                    sum += (short)(stream[offset] | (stream[offset + 1] << 8));
            }

            var sample = (short)Math.Clamp(sum, short.MinValue, short.MaxValue);
            mixed[offset] = (byte)(sample & 0xFF);
            mixed[offset + 1] = (byte)((sample >> 8) & 0xFF);
        }

        return mixed;
    }

    /// <summary>Bytes in one frame of <paramref name="frameMilliseconds"/> at the given format.</summary>
    internal static int FrameSizeInBytes(PcmAudioFormat format, int frameMilliseconds) =>
        format.SampleRateHz * frameMilliseconds / 1000
        * format.ChannelCount
        * (format.BitsPerSample / 8);
}
