using System.Buffers.Binary;
using System.Text;
using EasyChat.Infrastructure.MacOS.Audio;

namespace EasyChat.Infrastructure.MacOS.Tests.Audio;

[TestClass]
public sealed class WaveAudioTests
{
    [TestMethod]
    public void AToneIsAWellFormedWaveFileOfTheRequestedLength()
    {
        var wave = WaveAudio.CreateTone(880, 100);

        Assert.AreEqual("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
        Assert.AreEqual("WAVE", Encoding.ASCII.GetString(wave, 8, 4));
        Assert.AreEqual("data", Encoding.ASCII.GetString(wave, 36, 4));

        var dataLength = BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(40));
        Assert.AreEqual(wave.Length - 44, dataLength, "The declared data length must match the file.");
        Assert.AreEqual(
            BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(4)),
            wave.Length - 8,
            "The RIFF size counts everything after the first eight bytes.");

        // 100 ms at 44.1 kHz mono 16-bit.
        Assert.AreEqual(4410 * 2, dataLength);
    }

    [TestMethod]
    public void AToneStartsAndEndsAtSilenceSoItDoesNotClick()
    {
        var wave = WaveAudio.CreateTone(880, 100);

        Assert.AreEqual(0, ReadSample(wave, 0));
        Assert.AreEqual(0, ReadSample(wave, (wave.Length - 44) / 2 - 1), 64);
    }

    [TestMethod]
    public void AToneActuallyContainsSignal()
    {
        var wave = WaveAudio.CreateTone(880, 100);
        var peak = 0;
        for (var index = 0; index < (wave.Length - 44) / 2; index++)
            peak = Math.Max(peak, Math.Abs((int)ReadSample(wave, index)));

        Assert.IsGreaterThan(short.MaxValue / 8, peak, "The cue has to be audible.");
    }

    [TestMethod]
    public void DifferentPitchesProduceDifferentAudio()
    {
        var high = WaveAudio.CreateTone(880, 75);
        var low = WaveAudio.CreateTone(660, 75);

        Assert.AreNotEqual(Convert.ToHexString(high), Convert.ToHexString(low));
    }

    [TestMethod]
    public void AnInvalidToneRequestIsRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WaveAudio.CreateTone(0, 100));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WaveAudio.CreateTone(880, 0));
    }

    [TestMethod]
    public void MediaTypesMapToTheExtensionTheDecoderNeeds()
    {
        Assert.AreEqual(".mp3", WaveAudio.ExtensionFor("audio/mpeg"));
        Assert.AreEqual(".wav", WaveAudio.ExtensionFor("audio/wav"));
        Assert.AreEqual(".m4a", WaveAudio.ExtensionFor("audio/mp4"));
        Assert.AreEqual(".mp3", WaveAudio.ExtensionFor("AUDIO/MPEG"), "matching is case insensitive");

        // Edge TTS answers MPEG audio, so an unrecognised type is far more likely to be that than
        // anything else EasyChat receives.
        Assert.AreEqual(".mp3", WaveAudio.ExtensionFor(null));
        Assert.AreEqual(".mp3", WaveAudio.ExtensionFor("application/octet-stream"));
    }

    private static short ReadSample(byte[] wave, int index) =>
        BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(44 + (index * 2)));
}
