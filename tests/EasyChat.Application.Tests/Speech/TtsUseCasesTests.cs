using EasyChat.Application.Speech;
using EasyChat.Application.Tests.Settings;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Speech;
using EasyChat.Shared.Results;

namespace EasyChat.Application.Tests.Speech;

[TestClass]
public sealed class TtsUseCasesTests
{
    [TestMethod]
    public async Task ConfigurationFallbackVoicePreferenceAndInterruptedPlaybackUseOneProviderPolicy()
    {
        var initial = SettingsTestData.CreateBundle();
        var settings = new MutableSettingsUseCases(initial with
        {
            Tts = new TtsSettings(
                "missing",
                new Dictionary<string, IReadOnlyDictionary<string, string>>
                {
                    [TtsProviderIds.EdgeTts] = new Dictionary<string, string>
                    {
                        ["en"] = "en-US-ConfiguredNeural"
                    }
                })
        });
        var edge = new FakeProvider(TtsProviderIds.EdgeTts);
        var playback = new FakePlaybackQueue();
        var output = new FakeOutputWriter();
        var useCases = new TtsUseCases([edge, new FakeProvider("secondary")], settings, output, playback);

        var voice = await useCases.ResolvePreferredVoiceAsync("en");
        Assert.IsTrue(voice.IsSuccess);
        Assert.AreEqual("en-US-ConfiguredNeural", voice.Value);

        var request = new TtsSynthesisRequest("hello", "en-US-ConfiguredNeural");
        var queued = await useCases.EnqueueAsync(request, interruptCurrent: true);
        var saved = await useCases.SynthesizeToFileAsync(request, "voice.mp3");

        Assert.IsTrue(queued.IsSuccess);
        Assert.IsTrue(saved.IsSuccess);
        Assert.AreEqual(2, edge.SynthesisCount);
        CollectionAssert.AreEqual(new[] { "stop", "enqueue" }, playback.Operations);
        Assert.AreEqual("voice.mp3", output.Path);
    }

    [TestMethod]
    public async Task ResolvePreferredVoiceAsync_IgnoresRegionalLanguageSubtags()
    {
        var initial = SettingsTestData.CreateBundle();
        var settings = new MutableSettingsUseCases(initial with
        {
            Tts = new TtsSettings(
                TtsProviderIds.EdgeTts,
                new Dictionary<string, IReadOnlyDictionary<string, string>>
                {
                    [TtsProviderIds.EdgeTts] = new Dictionary<string, string>
                    {
                        ["ja-JP"] = "ja-JP-NanamiNeural"
                    }
                })
        });
        var useCases = new TtsUseCases(
            [new FakeProvider(TtsProviderIds.EdgeTts)],
            settings,
            new FakeOutputWriter(),
            new FakePlaybackQueue());

        var voice = await useCases.ResolvePreferredVoiceAsync("ja");

        Assert.IsTrue(voice.IsSuccess);
        Assert.AreEqual("ja-JP-NanamiNeural", voice.Value);
    }

    [TestMethod]
    public async Task ResolvePreferredVoiceAsync_PrefersConfiguredRegionalVoices()
    {
        var initial = SettingsTestData.CreateBundle();
        var settings = new MutableSettingsUseCases(initial with
        {
            Tts = new TtsSettings(
                TtsProviderIds.EdgeTts,
                new Dictionary<string, IReadOnlyDictionary<string, string>>
                {
                    [TtsProviderIds.EdgeTts] = new Dictionary<string, string>
                    {
                        ["en-GB"] = "en-GB-ConfiguredNeural",
                        ["en-US"] = "en-US-ConfiguredNeural"
                    }
                })
        });
        var useCases = new TtsUseCases(
            [new FakeProvider(TtsProviderIds.EdgeTts)],
            settings,
            new FakeOutputWriter(),
            new FakePlaybackQueue());

        var british = await useCases.ResolvePreferredVoiceAsync("en-GB");
        var american = await useCases.ResolvePreferredVoiceAsync("en-US");

        Assert.AreEqual("en-GB-ConfiguredNeural", british.Value);
        Assert.AreEqual("en-US-ConfiguredNeural", american.Value);
    }

    [TestMethod]
    public async Task ResolvePreferredVoiceAsync_DoesNotUseAnotherRegionAsGenericFallback()
    {
        var initial = SettingsTestData.CreateBundle();
        var settings = new MutableSettingsUseCases(initial with
        {
            Tts = new TtsSettings(
                TtsProviderIds.EdgeTts,
                new Dictionary<string, IReadOnlyDictionary<string, string>>
                {
                    [TtsProviderIds.EdgeTts] = new Dictionary<string, string>
                    {
                        ["en-GB"] = "en-GB-ConfiguredNeural"
                    }
                })
        });
        var useCases = new TtsUseCases(
            [new FakeProvider(TtsProviderIds.EdgeTts)],
            settings,
            new FakeOutputWriter(),
            new FakePlaybackQueue());

        var american = await useCases.ResolvePreferredVoiceAsync("en-US");

        Assert.AreEqual("en-US-DefaultNeural", american.Value);
    }

    [TestMethod]
    public async Task PlaybackSession_StopStopsQueueAndCompletesSession()
    {
        var settings = new MutableSettingsUseCases(SettingsTestData.CreateBundle());
        var playback = new FakePlaybackQueue();
        var useCases = new TtsUseCases(
            [new FakeProvider(TtsProviderIds.EdgeTts)],
            settings,
            new FakeOutputWriter(),
            playback);

        var result = await useCases.EnqueueWithSessionAsync(
            new TtsSynthesisRequest("hello", "en-US-DefaultNeural"),
            interruptCurrent: true);

        Assert.IsTrue(result.IsSuccess);
        await result.Value.StopAsync();
        await result.Value.Completion.WaitAsync(TimeSpan.FromSeconds(1));

        CollectionAssert.Contains(playback.Operations, "stop");
    }

    [TestMethod]
    public async Task PlaybackSession_CompletesWhenLifecycleReportsNaturalCompletion()
    {
        var settings = new MutableSettingsUseCases(SettingsTestData.CreateBundle());
        var playback = new FakePlaybackQueue();
        var useCases = new TtsUseCases(
            [new FakeProvider(TtsProviderIds.EdgeTts)],
            settings,
            new FakeOutputWriter(),
            playback);

        var result = await useCases.EnqueueWithSessionAsync(
            new TtsSynthesisRequest("hello", "en-US-DefaultNeural"),
            interruptCurrent: true);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.Value.Completion.IsCompleted);
        playback.CompleteLast();
        await result.Value.Completion.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task VirtualCableTargetIsValidatedAndForwardedToPlaybackQueue()
    {
        var settings = new MutableSettingsUseCases(SettingsTestData.CreateBundle());
        var playback = new FakePlaybackQueue();
        var useCases = new TtsUseCases(
            [new FakeProvider(TtsProviderIds.EdgeTts)],
            settings,
            new FakeOutputWriter(),
            playback,
            new FakePlaybackDeviceCatalog(hasVirtualCable: true));

        var result = await useCases.EnqueueAsync(
            new TtsSynthesisRequest("hello", "en-US-DefaultNeural"),
            interruptCurrent: false,
            target: AudioPlaybackTarget.VirtualCable);

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.Contains(playback.Targets, AudioPlaybackTarget.VirtualCable);
    }

    [TestMethod]
    public async Task PlaybackContextIsForwardedForDefaultTarget()
    {
        var settings = new MutableSettingsUseCases(SettingsTestData.CreateBundle());
        var playback = new FakePlaybackQueue();
        var useCases = new TtsUseCases(
            [new FakeProvider(TtsProviderIds.EdgeTts)],
            settings,
            new FakeOutputWriter(),
            playback);

        var result = await useCases.EnqueueAsync(
            new TtsSynthesisRequest("hello", "en-US-DefaultNeural"),
            interruptCurrent: false,
            target: AudioPlaybackTarget.Default,
            context: AudioPlaybackContext.RealtimeInterpretation);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(AudioPlaybackContext.RealtimeInterpretation, playback.Contexts.Single());
    }

    [TestMethod]
    public async Task SelectedOutputDeviceAndPlaybackContextAreForwarded()
    {
        var settings = new MutableSettingsUseCases(SettingsTestData.CreateBundle());
        var playback = new FakePlaybackQueue();
        ITtsUseCases useCases = new TtsUseCases(
            [new FakeProvider(TtsProviderIds.EdgeTts)],
            settings,
            new FakeOutputWriter(),
            playback,
            new FakePlaybackDeviceCatalog(hasVirtualCable: false, selectedDeviceName: "Speakers"));

        var result = await useCases.EnqueueAsync(
            new TtsSynthesisRequest("hello", "en-US-DefaultNeural"),
            interruptCurrent: false,
            target: AudioPlaybackTarget.SelectedDevice,
            context: AudioPlaybackContext.RealtimeInterpretation,
            outputDeviceName: "Speakers");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(AudioPlaybackTarget.SelectedDevice, playback.Targets.Single());
        Assert.AreEqual(AudioPlaybackContext.RealtimeInterpretation, playback.Contexts.Single());
        Assert.AreEqual(
            new AudioPlaybackDeviceToken("test:speakers", "Speakers"),
            playback.DeviceTokens.Single());
    }

    [TestMethod]
    public async Task VirtualCableTargetFailsWithoutCableAndDoesNotEnqueue()
    {
        var settings = new MutableSettingsUseCases(SettingsTestData.CreateBundle());
        var playback = new FakePlaybackQueue();
        var useCases = new TtsUseCases(
            [new FakeProvider(TtsProviderIds.EdgeTts)],
            settings,
            new FakeOutputWriter(),
            playback,
            new FakePlaybackDeviceCatalog(hasVirtualCable: false));

        var result = await useCases.EnqueueAsync(
            new TtsSynthesisRequest("hello", "en-US-DefaultNeural"),
            interruptCurrent: false,
            target: AudioPlaybackTarget.VirtualCable);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual("audio.virtual_cable_unavailable", result.Error.Code);
        Assert.IsEmpty(playback.Targets);
    }

    private sealed class FakeProvider(string providerId) : ITtsSynthesisProvider
    {
        public string ProviderId { get; } = providerId;
        public int SynthesisCount { get; private set; }

        public ValueTask<Result<IReadOnlyList<TtsVoice>>> GetVoicesAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<IReadOnlyList<TtsVoice>>.Success([
                new TtsVoice("en-GB-DefaultNeural", "British", "en-GB", "Female", [], []),
                new TtsVoice("en-US-DefaultNeural", "American", "en-US", "Female", [], [])
            ]));

        public ValueTask<Result<IReadOnlyList<TtsLanguage>>> GetLanguagesAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<IReadOnlyList<TtsLanguage>>.Success([]));

        public ValueTask<Result<AudioTrack>> SynthesizeAsync(
            TtsSynthesisRequest request,
            CancellationToken cancellationToken = default)
        {
            SynthesisCount++;
            return ValueTask.FromResult(Result<AudioTrack>.Success(
                new AudioTrack(new byte[] { 1, 2, 3 }, "audio/mpeg")));
        }
    }

    private sealed class FakePlaybackQueue : IAudioPlaybackQueue, IAudioPlaybackLifecycle
    {
        public List<string> Operations { get; } = [];
        public List<AudioPlaybackTarget> Targets { get; } = [];
        public List<AudioPlaybackContext> Contexts { get; } = [];
        public List<AudioPlaybackDeviceToken?> DeviceTokens { get; } = [];
        public List<Guid> PlaybackIds { get; } = [];
        event EventHandler<AudioPlaybackEventArgs>? IAudioPlaybackLifecycle.PlaybackStarted
        {
            add { }
            remove { }
        }
        public event EventHandler<AudioPlaybackEventArgs>? PlaybackCompleted;

        public ValueTask<Guid> EnqueueAsync(AudioTrack track, CancellationToken cancellationToken = default)
        {
            Operations.Add("enqueue");
            var id = Guid.NewGuid();
            PlaybackIds.Add(id);
            return ValueTask.FromResult(id);
        }

        public ValueTask<Guid> EnqueueAsync(
            AudioTrack track,
            AudioPlaybackTarget target,
            CancellationToken cancellationToken = default)
        {
            Operations.Add($"enqueue:{target}");
            Targets.Add(target);
            var id = Guid.NewGuid();
            PlaybackIds.Add(id);
            return ValueTask.FromResult(id);
        }

        public ValueTask<Guid> EnqueueAsync(
            AudioTrack track,
            AudioPlaybackTarget target,
            AudioPlaybackContext context,
            CancellationToken cancellationToken = default)
        {
            Operations.Add($"enqueue:{target}:{context}");
            Targets.Add(target);
            Contexts.Add(context);
            var id = Guid.NewGuid();
            PlaybackIds.Add(id);
            return ValueTask.FromResult(id);
        }

        public ValueTask<Guid> EnqueueAsync(
            AudioTrack track,
            AudioPlaybackTarget target,
            AudioPlaybackContext context,
            AudioPlaybackDeviceToken? deviceToken,
            CancellationToken cancellationToken = default)
        {
            Operations.Add($"enqueue:{target}:{context}:{deviceToken?.Value}");
            Targets.Add(target);
            Contexts.Add(context);
            DeviceTokens.Add(deviceToken);
            var id = Guid.NewGuid();
            PlaybackIds.Add(id);
            return ValueTask.FromResult(id);
        }

        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            Operations.Add("stop");
            return ValueTask.CompletedTask;
        }

        public void CompleteLast() => PlaybackCompleted?.Invoke(
            this,
            new AudioPlaybackEventArgs(
                PlaybackIds[^1],
                AudioPlaybackTarget.Default,
                AudioPlaybackContext.None));
    }

    private sealed class FakePlaybackDeviceCatalog(
        bool hasVirtualCable,
        string? selectedDeviceName = null) : IAudioPlaybackDeviceCatalog
    {
        public ValueTask<IReadOnlyList<AudioPlaybackDeviceDescriptor>> GetDevicesAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<AudioPlaybackDeviceDescriptor>>(
                (hasVirtualCable, selectedDeviceName) switch
                {
                    (true, not null) => [
                        new(
                            new AudioPlaybackDeviceToken("test:cable"),
                            "CABLE Input",
                            "CABLE Input",
                            null,
                            true),
                        new(
                            new AudioPlaybackDeviceToken("test:speakers", selectedDeviceName),
                            selectedDeviceName,
                            selectedDeviceName,
                            null,
                            false)],
                    (true, null) => [new(
                        new AudioPlaybackDeviceToken("test:cable"),
                        "CABLE Input",
                        "CABLE Input",
                        null,
                        true)],
                    (false, not null) => [new(
                        new AudioPlaybackDeviceToken("test:speakers", selectedDeviceName),
                        selectedDeviceName,
                        selectedDeviceName,
                        null,
                        false)],
                    _ => []
                });
    }

    private sealed class FakeOutputWriter : ITtsOutputWriter
    {
        public string? Path { get; private set; }

        public ValueTask<Result> WriteAsync(
            string path,
            AudioTrack track,
            CancellationToken cancellationToken = default)
        {
            Path = path;
            return ValueTask.FromResult(Result.Success());
        }
    }
}
