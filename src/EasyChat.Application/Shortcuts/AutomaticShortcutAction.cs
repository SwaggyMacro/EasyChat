using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Shortcuts;
using EasyChat.Shared.Results;

namespace EasyChat.Application.Shortcuts;

public sealed class AutomaticShortcutAction : IAutomaticShortcutAction, IDisposable
{
    private readonly IAudioPlaybackLifecycle _playback;
    private readonly IKeyboardInput _keyboard;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, IReadOnlyList<AutomaticBinding>> _held = [];
    private readonly HashSet<Guid> _pendingHold = [];
    private readonly HashSet<Guid> _completedBeforeStart = [];
    private IReadOnlyList<AutomaticBinding> _bindings = [];
    private int _configurationVersion;
    private int _disposed;

    public AutomaticShortcutAction(
        IAudioPlaybackLifecycle playback,
        IKeyboardInput keyboard)
    {
        _playback = playback ?? throw new ArgumentNullException(nameof(playback));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _playback.PlaybackStarted += OnPlaybackStarted;
        _playback.PlaybackCompleted += OnPlaybackCompleted;
    }

    public string ActionType => "AutomaticKey";

    public bool PreventConcurrentExecution => false;

    public ValueTask ExecuteAsync(
        ShortcutParameterSettings? parameter,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public async ValueTask<IReadOnlyList<ShortcutRegistrationIssue>> ConfigureAsync(
        IReadOnlyList<ShortcutEntrySettings> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        cancellationToken.ThrowIfCancellationRequested();
        await ResetAsync(cancellationToken).ConfigureAwait(false);

        var bindings = new List<AutomaticBinding>();
        var issues = new List<ShortcutRegistrationIssue>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Parameter is null)
            {
                issues.Add(Issue(entry, "automatic-shortcut.parameter-missing", "Automatic shortcut settings are missing."));
                continue;
            }

            var validation = _keyboard.ValidateCombination(entry.KeyCombination);
            if (validation.IsFailure)
            {
                issues.Add(Issue(entry, validation.Error.Code, validation.Error.Message));
                continue;
            }

            var trigger = entry.Parameter.AutomaticTrigger ?? AutomaticShortcutTrigger.RealtimeInterpretationPlayback;
            if (trigger != AutomaticShortcutTrigger.RealtimeInterpretationPlayback)
            {
                issues.Add(Issue(entry, "automatic-shortcut.trigger-unsupported", "The automatic shortcut trigger is not supported."));
                continue;
            }

            bindings.Add(new AutomaticBinding(
                entry.KeyCombination,
                entry.Parameter.AutomaticPressMode ?? AutomaticShortcutPressMode.Tap));
        }

        lock (_sync)
        {
            _bindings = bindings;
            _configurationVersion++;
        }
        return issues;
    }

    public async ValueTask ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<AutomaticBinding> held;
        lock (_sync)
        {
            _bindings = [];
            _configurationVersion++;
            held = _held.Values.SelectMany(value => value).ToArray();
            _held.Clear();
            _pendingHold.Clear();
            _completedBeforeStart.Clear();
        }

        foreach (var binding in held)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _keyboard.ReleaseAsync(binding.KeyCombination, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _playback.PlaybackStarted -= OnPlaybackStarted;
        _playback.PlaybackCompleted -= OnPlaybackCompleted;
        try
        {
            ResetAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
        }
    }

    private void OnPlaybackStarted(object? sender, AudioPlaybackEventArgs args)
    {
        if (args.Context != AudioPlaybackContext.RealtimeInterpretation || Volatile.Read(ref _disposed) != 0)
            return;
        _ = HandleStartedAsync(args);
    }

    private void OnPlaybackCompleted(object? sender, AudioPlaybackEventArgs args)
    {
        if (args.Context != AudioPlaybackContext.RealtimeInterpretation || Volatile.Read(ref _disposed) != 0)
            return;
        _ = HandleCompletedAsync(args);
    }

    private async Task HandleStartedAsync(AudioPlaybackEventArgs args)
    {
        AutomaticBinding[] bindings;
        int configurationVersion;
        lock (_sync)
        {
            bindings = _bindings.ToArray();
            configurationVersion = _configurationVersion;
        }

        var hasHoldBinding = bindings.Any(binding => binding.PressMode == AutomaticShortcutPressMode.Hold);
        if (hasHoldBinding)
        {
            lock (_sync)
                _pendingHold.Add(args.PlaybackId);
        }

        var held = new List<AutomaticBinding>();
        foreach (var binding in bindings)
        {
            try
            {
                var result = binding.PressMode == AutomaticShortcutPressMode.Hold
                    ? await _keyboard.PressAsync(binding.KeyCombination).ConfigureAwait(false)
                    : await _keyboard.TapAsync(binding.KeyCombination).ConfigureAwait(false);
                if (result.IsSuccess && binding.PressMode == AutomaticShortcutPressMode.Hold)
                    held.Add(binding);
            }
            catch
            {
            }
        }

        if (held.Count > 0)
        {
            var releaseImmediately = false;
            lock (_sync)
            {
                _pendingHold.Remove(args.PlaybackId);
                if (_configurationVersion != configurationVersion
                    || _completedBeforeStart.Remove(args.PlaybackId))
                    releaseImmediately = true;
                else
                    _held[args.PlaybackId] = held;
            }
            if (releaseImmediately)
            {
                foreach (var binding in held)
                {
                    try
                    {
                        await _keyboard.ReleaseAsync(binding.KeyCombination).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }
        }
        else if (hasHoldBinding)
        {
            lock (_sync)
            {
                _pendingHold.Remove(args.PlaybackId);
                _completedBeforeStart.Remove(args.PlaybackId);
            }
        }
    }

    private async Task HandleCompletedAsync(AudioPlaybackEventArgs args)
    {
        IReadOnlyList<AutomaticBinding>? held;
        lock (_sync)
        {
            _held.TryGetValue(args.PlaybackId, out held);
            _held.Remove(args.PlaybackId);
        }

        if (held is null)
        {
            lock (_sync)
            {
                if (_pendingHold.Remove(args.PlaybackId))
                    _completedBeforeStart.Add(args.PlaybackId);
            }
            return;
        }
        foreach (var binding in held)
        {
            try
            {
                await _keyboard.ReleaseAsync(binding.KeyCombination).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private static ShortcutRegistrationIssue Issue(
        ShortcutEntrySettings entry,
        string code,
        string message) =>
        new(entry.ActionType, entry.KeyCombination, new Error(code, message));

    private readonly record struct AutomaticBinding(
        string KeyCombination,
        AutomaticShortcutPressMode PressMode);
}
