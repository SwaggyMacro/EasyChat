using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Audio;

namespace EasyChat.Infrastructure.MacOS.Tests.Audio;

/// <summary>
/// Sample-rate conversion, channel downmix and framing are where an audio path goes quietly wrong:
/// half-speed audio, a dropped channel or misaligned frames all still sound like audio and only show
/// up as worse recognition. They are asserted against known samples here.
/// </summary>
[TestClass]
public sealed class PcmAudioConversionTests
{
    private static readonly PcmAudioFormat Target = PcmAudioFormat.SpeechRecognition;

    [TestMethod]
    public void MonoAudioAtTheTargetRatePassesThroughSampleForSample()
    {
        float[] samples = [0f, 0.5f, -0.5f, 1f, -1f];

        var pcm = PcmAudioConversion.FromFloat32(samples, 16_000, 1, Target);

        Assert.HasCount(samples.Length * 2, pcm);
        Assert.AreEqual(0, ReadSample(pcm, 0));
        Assert.AreEqual(short.MaxValue / 2, ReadSample(pcm, 1), 1);
        Assert.AreEqual(-short.MaxValue / 2, ReadSample(pcm, 2), 1);
        Assert.AreEqual(short.MaxValue, ReadSample(pcm, 3));
        Assert.AreEqual(-short.MaxValue, ReadSample(pcm, 4));
    }

    [TestMethod]
    public void StereoIsAveragedRatherThanSummed()
    {
        // Left at full scale and right at silence must land at half scale, not at full scale and
        // not clipped: summing instead of averaging would make every stereo source twice as loud.
        float[] interleaved = [1f, 0f, 1f, 0f];

        var pcm = PcmAudioConversion.FromFloat32(interleaved, 16_000, 2, Target);

        Assert.HasCount(4, pcm);
        Assert.AreEqual(short.MaxValue / 2, ReadSample(pcm, 0), 1);
    }

    [TestMethod]
    public void ADeviceRateIsResampledToTheRecognitionRate()
    {
        // 48 kHz is what almost every Mac device runs at, and it must come out three times shorter.
        var samples = new float[4800];
        for (var index = 0; index < samples.Length; index++)
            samples[index] = MathF.Sin(index * 0.01f);

        var pcm = PcmAudioConversion.FromFloat32(samples, 48_000, 1, Target);

        Assert.HasCount(1600 * 2, pcm, "100 ms at 16 kHz is 1600 samples.");
    }

    [TestMethod]
    public void ResamplingPreservesTheSignalRatherThanDecimatingBlindly()
    {
        // A constant input must stay constant through the interpolation; a naive index step that
        // drifted would show up here as a varying level.
        var samples = new float[960];
        Array.Fill(samples, 0.25f);

        var pcm = PcmAudioConversion.FromFloat32(samples, 48_000, 1, Target);

        for (var index = 0; index < pcm.Length / 2; index++)
            Assert.AreEqual(short.MaxValue / 4, ReadSample(pcm, index), 2, $"sample {index}");
    }

    [TestMethod]
    public void SamplesBeyondFullScaleAreClampedNotWrapped()
    {
        // Wrapping would turn a loud passage into a burst of noise, which is far worse than
        // clipping it.
        float[] samples = [4f, -4f];

        var pcm = PcmAudioConversion.FromFloat32(samples, 16_000, 1, Target);

        Assert.AreEqual(short.MaxValue, ReadSample(pcm, 0));
        Assert.AreEqual(-short.MaxValue, ReadSample(pcm, 1));
    }

    [TestMethod]
    public void AnEmptyBufferProducesNoAudioRatherThanThrowing() =>
        Assert.IsEmpty(PcmAudioConversion.FromFloat32([], 48_000, 2, Target));

    [TestMethod]
    public void AnUnsupportedOutputShapeIsRejected() =>
        Assert.ThrowsExactly<NotSupportedException>(() =>
            PcmAudioConversion.FromFloat32([0f], 16_000, 1, new PcmAudioFormat(16_000, 2, 16)));

    [TestMethod]
    public void MixingTwoSourcesSumsThem()
    {
        var first = Pcm(1000, 2000);
        var second = Pcm(500, -1000);

        var mixed = PcmAudioConversion.Mix([first, second]);

        Assert.AreEqual(1500, ReadSample(mixed, 0));
        Assert.AreEqual(1000, ReadSample(mixed, 1));
    }

    [TestMethod]
    public void MixingClampsInsteadOfWrapping()
    {
        var loud = Pcm(short.MaxValue);

        var mixed = PcmAudioConversion.Mix([loud, loud, loud]);

        Assert.AreEqual(short.MaxValue, ReadSample(mixed, 0));
    }

    [TestMethod]
    public void AShorterSourceContributesSilenceForTheFramesItDoesNotCover()
    {
        var longer = Pcm(100, 200, 300);
        var shorter = Pcm(50);

        var mixed = PcmAudioConversion.Mix([longer, shorter]);

        Assert.HasCount(6, mixed);
        Assert.AreEqual(150, ReadSample(mixed, 0));
        Assert.AreEqual(200, ReadSample(mixed, 1));
        Assert.AreEqual(300, ReadSample(mixed, 2));
    }

    [TestMethod]
    public void ASingleSourceIsPassedThroughUnchanged()
    {
        var only = Pcm(1, 2, 3);

        Assert.AreSame(only, PcmAudioConversion.Mix([only]));
    }

    [TestMethod]
    public void AFrameIsTwentyMillisecondsOfAudio() =>
        // 16 kHz mono 16-bit: 320 samples, 640 bytes.
        Assert.AreEqual(640, PcmAudioConversion.FrameSizeInBytes(Target, 20));

    private static short ReadSample(byte[] pcm, int index) =>
        (short)(pcm[index * 2] | (pcm[(index * 2) + 1] << 8));

    private static byte[] Pcm(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var index = 0; index < samples.Length; index++)
        {
            bytes[index * 2] = (byte)(samples[index] & 0xFF);
            bytes[(index * 2) + 1] = (byte)((samples[index] >> 8) & 0xFF);
        }

        return bytes;
    }
}
