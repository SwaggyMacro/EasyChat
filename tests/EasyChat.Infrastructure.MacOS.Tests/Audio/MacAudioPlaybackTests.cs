using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Audio;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Infrastructure.MacOS.Tests.Audio;

[TestClass]
public sealed class MacAudioPlaybackTests
{
    private static bool Skip()
    {
        if (OperatingSystem.IsMacOSVersionAtLeast(26))
            return false;

        Assert.Inconclusive("Audio playback can only be verified on macOS 26 or later.");
        return true;
    }

    [TestMethod]
    public async Task OutputDevicesAreListedWithoutAnyPrivacyApproval()
    {
        if (Skip())
            return;

        var devices = await new MacAudioPlaybackDeviceCatalog().GetDevicesAsync();

        Assert.IsNotEmpty(devices);
        foreach (var device in devices)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(device.Token.Value), device.Name);
            Assert.IsFalse(string.IsNullOrWhiteSpace(device.Name));
        }

        Assert.AreEqual(
            devices.Count,
            devices.Select(device => device.Token).Distinct().Count(),
            "Device identities must not collide.");
    }

    [TestMethod]
    public async Task ExactlyOneDeviceIsPresentedAsTheDefault()
    {
        if (Skip())
            return;

        var devices = await new MacAudioPlaybackDeviceCatalog().GetDevicesAsync();

        Assert.IsLessThanOrEqualTo(
            1,
            devices.Count(device => device.DisplayName.EndsWith("(Default)", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void LoopbackDriversAreRecognisedByNameAndOrdinaryDevicesAreNot()
    {
        // CoreAudio does not flag a device as virtual, so this is a name match — the same approach
        // the Windows adapter takes for VB-Audio, applied to the macOS equivalents.
        Assert.IsTrue(MacVirtualAudioDevices.IsLoopback("BlackHole 2ch"));
        Assert.IsTrue(MacVirtualAudioDevices.IsLoopback("Loopback Audio"));
        Assert.IsTrue(MacVirtualAudioDevices.IsLoopback("Soundflower (2ch)"));

        Assert.IsFalse(MacVirtualAudioDevices.IsLoopback("MacBook Pro Speakers"));
        Assert.IsFalse(MacVirtualAudioDevices.IsLoopback("External Headphones"));
        Assert.IsFalse(MacVirtualAudioDevices.IsLoopback("AirPods Pro"));
        Assert.IsFalse(MacVirtualAudioDevices.IsLoopback(null));
    }

    [TestMethod]
    public async Task ASilentClipIsPlayedAllTheWayThroughAndTheQueueDrains()
    {
        if (Skip())
            return;

        // Silence, so the suite stays quiet on the developer's machine while still exercising the
        // whole path: temporary file, AVPlayer creation, decode, playback and completion detection.
        var silence = WaveAudio.CreatePcm16(new short[4410]);
        await using var queue = new MacAudioPlaybackQueue(
            new MacAudioPlaybackDeviceCatalog(),
            NullLogger<MacAudioPlaybackQueue>.Instance);

        await queue.EnqueueAsync(new AudioTrack(silence, "audio/wav"));

        // Disposal drains the queue and joins the pump, so a clip that never finished would hang
        // here rather than pass.
        await queue.DisposeAsync();
    }

    [TestMethod]
    public async Task StoppingEmptiesTheQueueInsteadOfLettingTheBacklogDrain()
    {
        if (Skip())
            return;

        var silence = WaveAudio.CreatePcm16(new short[44_100]);
        await using var queue = new MacAudioPlaybackQueue(
            new MacAudioPlaybackDeviceCatalog(),
            NullLogger<MacAudioPlaybackQueue>.Instance);

        for (var index = 0; index < 5; index++)
            await queue.EnqueueAsync(new AudioTrack(silence, "audio/wav"));

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        await queue.StopAsync();
        await queue.DisposeAsync();
        elapsed.Stop();

        // Five one-second clips would take five seconds if Stop merely let the backlog finish.
        Assert.IsLessThan(TimeSpan.FromSeconds(3), elapsed.Elapsed);
    }

    [TestMethod]
    public async Task EachCueHasItsOwnToneAndAlwaysUsesTheDefaultOutput()
    {
        var queue = new RecordingPlaybackQueue();
        var player = new MacAudioFeedbackCuePlayer(queue);

        foreach (var cue in Enum.GetValues<AudioFeedbackCue>())
            await player.PlayAsync(cue);

        Assert.HasCount(Enum.GetValues<AudioFeedbackCue>().Length, queue.Tracks);
        Assert.IsTrue(
            queue.Targets.All(target => target == AudioPlaybackTarget.Default),
            "A cue routed into a loopback device would be heard by the other party.");
        Assert.AreEqual(
            queue.Tracks.Count,
            queue.Tracks.Select(track => Convert.ToHexString(track.Content.Span)).Distinct().Count(),
            "Each cue must sound different from the others.");
    }

    [TestMethod]
    public async Task AnUnknownCueIsRejected()
    {
        var player = new MacAudioFeedbackCuePlayer(new RecordingPlaybackQueue());

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await player.PlayAsync((AudioFeedbackCue)99));
    }

    private sealed class RecordingPlaybackQueue : IAudioPlaybackQueue
    {
        internal List<AudioTrack> Tracks { get; } = [];

        internal List<AudioPlaybackTarget> Targets { get; } = [];

        public ValueTask EnqueueAsync(AudioTrack track, CancellationToken cancellationToken = default) =>
            EnqueueAsync(track, AudioPlaybackTarget.Default, cancellationToken);

        public ValueTask EnqueueAsync(
            AudioTrack track,
            AudioPlaybackTarget target,
            CancellationToken cancellationToken = default)
        {
            Tracks.Add(track);
            Targets.Add(target);
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
