using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Audio;
using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS.Tests.Audio;

[TestClass]
public sealed class MacAudioCaptureSourceCatalogTests
{
    private static bool Skip()
    {
        if (OperatingSystem.IsMacOSVersionAtLeast(26))
            return false;

        Assert.Inconclusive("Audio sources can only be verified on macOS 26 or later.");
        return true;
    }

    [TestMethod]
    public async Task TheCatalogIsPopulatedWithoutAnyPrivacyApproval()
    {
        if (Skip())
            return;

        // Listing devices and applications needs no microphone or screen recording approval, which
        // is what lets the picker be useful before EasyChat asks the user for anything.
        var sources = await new MacAudioCaptureSourceCatalog().GetSourcesAsync();

        Assert.IsNotEmpty(sources);
        Assert.AreEqual(
            1,
            sources.Count(source => source.Kind == AudioCaptureSourceKind.SystemOutput),
            "There is exactly one system output source.");
        Assert.IsTrue(
            sources.Any(source => source.Kind == AudioCaptureSourceKind.Microphone),
            "A Mac always has at least one input device.");
    }

    [TestMethod]
    public async Task EverySourceCarriesATokenThisAdapterCanDecode()
    {
        if (Skip())
            return;

        var sources = await new MacAudioCaptureSourceCatalog().GetSourcesAsync();

        foreach (var source in sources)
        {
            Assert.IsTrue(
                MacAudioSourceTokens.TryDecode(source.Token, out var decoded),
                source.Token.Value);
            Assert.AreEqual(source.Kind, decoded.Kind, source.Token.Value);
            Assert.IsFalse(string.IsNullOrWhiteSpace(source.Name), source.Token.Value);
        }
    }

    [TestMethod]
    public async Task TheCatalogNeverOffersEasyChatsOwnAudio()
    {
        if (Skip())
            return;

        // Capturing EasyChat itself would feed its own subtitles and speech back into recognition.
        var sources = await new MacAudioCaptureSourceCatalog().GetSourcesAsync();

        foreach (var source in sources.Where(item => item.Kind == AudioCaptureSourceKind.Application))
        {
            Assert.IsTrue(MacAudioSourceTokens.TryDecode(source.Token, out var decoded));
            Assert.AreNotEqual(Environment.ProcessId, decoded.ProcessIdentifier, source.Name);
        }
    }

    [TestMethod]
    public async Task AtMostOneInputDeviceIsMarkedAsTheDefault()
    {
        if (Skip())
            return;

        var sources = await new MacAudioCaptureSourceCatalog().GetSourcesAsync();

        Assert.IsLessThanOrEqualTo(
            1,
            sources.Count(source =>
                source.Kind == AudioCaptureSourceKind.Microphone && source.IsDefault));
    }

    [TestMethod]
    public void InputDevicesAreDistinguishedFromOutputOnlyDevicesByTheirChannels()
    {
        if (Skip())
            return;

        // A speaker reports no input channels, which is how it is excluded without guessing from
        // its name.
        var devices = CoreAudioNative.InputDevices();

        Assert.IsNotEmpty(devices);
        foreach (var device in devices)
        {
            Assert.IsGreaterThan(0, device.ChannelCount, device.Name);
            Assert.IsFalse(string.IsNullOrWhiteSpace(device.Uid), device.Name);
        }
    }

    [TestMethod]
    public void TokensRoundTripAndForeignTokensAreRejected()
    {
        Assert.IsTrue(MacAudioSourceTokens.TryDecode(
            MacAudioSourceTokens.ForSystemOutput(),
            out var system));
        Assert.AreEqual(AudioCaptureSourceKind.SystemOutput, system.Kind);

        Assert.IsTrue(MacAudioSourceTokens.TryDecode(
            MacAudioSourceTokens.ForApplication(4321),
            out var application));
        Assert.AreEqual(4321, application.ProcessIdentifier);

        Assert.IsTrue(MacAudioSourceTokens.TryDecode(
            MacAudioSourceTokens.ForMicrophone("BuiltInMicrophoneDevice"),
            out var microphone));
        Assert.AreEqual("BuiltInMicrophoneDevice", microphone.Identifier);

        Assert.IsFalse(MacAudioSourceTokens.TryDecode(new AudioCaptureSourceToken("windows:0"), out _));
        Assert.IsFalse(MacAudioSourceTokens.TryDecode(new AudioCaptureSourceToken(""), out _));
        Assert.IsFalse(MacAudioSourceTokens.TryDecode(
            new AudioCaptureSourceToken("macos:application:not-a-pid"),
            out _));
        Assert.IsFalse(MacAudioSourceTokens.TryDecode(
            new AudioCaptureSourceToken("macos:microphone:"),
            out _));
    }
}
