using EasyChat.Application.Shortcuts;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Shortcuts;
using EasyChat.Shared.Results;

namespace EasyChat.Application.Tests.Shortcuts;

[TestClass]
public sealed class AutomaticShortcutActionTests
{
    [TestMethod]
    public async Task TapBindingSendsOneTapForRealtimePlayback()
    {
        var playback = new FakePlaybackLifecycle();
        var keyboard = new RecordingKeyboardInput();
        using var action = new AutomaticShortcutAction(playback, keyboard);

        var issue = await action.ConfigureAsync([
            Entry(AutomaticShortcutPressMode.Tap)]);
        Assert.IsEmpty(issue);

        playback.Start(AudioPlaybackContext.RealtimeInterpretation);
        await keyboard.WaitForCountAsync(1);

        CollectionAssert.AreEqual(new[] { "tap:V" }, keyboard.Events.ToArray());
    }

    [TestMethod]
    public async Task HoldBindingReleasesAfterMatchingPlaybackCompletes()
    {
        var playback = new FakePlaybackLifecycle();
        var keyboard = new RecordingKeyboardInput();
        using var action = new AutomaticShortcutAction(playback, keyboard);
        var issue = await action.ConfigureAsync([
            Entry(AutomaticShortcutPressMode.Hold)]);
        Assert.IsEmpty(issue);

        var id = playback.Start(AudioPlaybackContext.RealtimeInterpretation);
        await keyboard.WaitForCountAsync(1);
        Assert.IsFalse(keyboard.Events.Contains("release:V"));

        playback.Complete(id, AudioPlaybackContext.RealtimeInterpretation);
        await keyboard.WaitForCountAsync(2);

        CollectionAssert.AreEqual(new[] { "press:V", "release:V" }, keyboard.Events.ToArray());
    }

    [TestMethod]
    public async Task NonRealtimePlaybackDoesNotTriggerBinding()
    {
        var playback = new FakePlaybackLifecycle();
        var keyboard = new RecordingKeyboardInput();
        using var action = new AutomaticShortcutAction(playback, keyboard);
        await action.ConfigureAsync([Entry(AutomaticShortcutPressMode.Hold)]);

        var id = playback.Start(AudioPlaybackContext.None);
        playback.Complete(id, AudioPlaybackContext.None);
        await Task.Delay(50);

        Assert.IsEmpty(keyboard.Events);
    }

    private static ShortcutEntrySettings Entry(AutomaticShortcutPressMode mode) =>
        new(
            "AutomaticKey",
            new ShortcutParameterSettings(
                string.Empty, null, null, null, null, null, null, null, null, null, null,
                AutomaticShortcutTrigger.RealtimeInterpretationPlayback,
                mode),
            "V",
            true);

    private sealed class FakePlaybackLifecycle : IAudioPlaybackLifecycle
    {
        public event EventHandler<AudioPlaybackEventArgs>? PlaybackStarted;
        public event EventHandler<AudioPlaybackEventArgs>? PlaybackCompleted;

        public Guid Start(AudioPlaybackContext context)
        {
            var id = Guid.NewGuid();
            PlaybackStarted?.Invoke(this, new AudioPlaybackEventArgs(id, AudioPlaybackTarget.VirtualCable, context));
            return id;
        }

        public void Complete(Guid id, AudioPlaybackContext context) =>
            PlaybackCompleted?.Invoke(this, new AudioPlaybackEventArgs(id, AudioPlaybackTarget.VirtualCable, context));
    }

    private sealed class RecordingKeyboardInput : IKeyboardInput
    {
        private readonly object _sync = new();
        private readonly List<string> _events = [];
        private readonly TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> Events
        {
            get { lock (_sync) return _events.ToArray(); }
        }

        public Result ValidateCombination(string combination) => Result.Success();

        public ValueTask<Result> PressAsync(string combination, CancellationToken cancellationToken = default)
        {
            Add($"press:{combination}");
            return ValueTask.FromResult(Result.Success());
        }

        public ValueTask<Result> ReleaseAsync(string combination, CancellationToken cancellationToken = default)
        {
            Add($"release:{combination}");
            return ValueTask.FromResult(Result.Success());
        }

        public ValueTask<Result> TapAsync(string combination, CancellationToken cancellationToken = default)
        {
            Add($"tap:{combination}");
            return ValueTask.FromResult(Result.Success());
        }

        public async Task WaitForCountAsync(int count)
        {
            while (Events.Count < count)
            {
                await Task.WhenAny(_changed.Task, Task.Delay(1000));
                if (Events.Count < count)
                    continue;
                break;
            }
        }

        private void Add(string value)
        {
            lock (_sync)
                _events.Add(value);
            _changed.TrySetResult();
        }
    }
}
