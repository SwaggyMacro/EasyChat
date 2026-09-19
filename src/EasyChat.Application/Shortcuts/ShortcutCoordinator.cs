using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Shortcuts;
using EasyChat.Shared.Results;

namespace EasyChat.Application.Shortcuts;

public sealed class ShortcutCoordinator : IShortcutUseCases
{
    private readonly ISettingsUseCases _settings;
    private readonly IPlatformAccessUseCases _platformAccess;
    private readonly IGlobalHotkeys _hotkeys;
    private readonly IAutomaticShortcutAction _automaticAction;
    private readonly IReadOnlyDictionary<string, ActionState> _actions;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private readonly List<IHotkeyRegistration> _hotkeyRegistrations = [];
    private int _started;
    private int _disposed;

    public ShortcutCoordinator(
        ISettingsUseCases settings,
        IPlatformAccessUseCases platformAccess,
        IGlobalHotkeys hotkeys,
        IEnumerable<IShortcutAction> actions,
        IAutomaticShortcutAction automaticAction)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _platformAccess = platformAccess ?? throw new ArgumentNullException(nameof(platformAccess));
        _hotkeys = hotkeys ?? throw new ArgumentNullException(nameof(hotkeys));
        _automaticAction = automaticAction ?? throw new ArgumentNullException(nameof(automaticAction));
        ArgumentNullException.ThrowIfNull(actions);
        _actions = actions.ToDictionary(
            action => action.ActionType,
            action => new ActionState(action),
            StringComparer.OrdinalIgnoreCase);
    }

    public async ValueTask<ShortcutRegistrationReport> StartAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
            _settings.SettingsChanged += OnSettingsChanged;

        return await ReloadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ShortcutRegistrationReport> ReloadAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DisposeHotkeyRegistrations();
            await _automaticAction.ResetAsync(cancellationToken).ConfigureAwait(false);
            var entries = _settings.Current.Shortcut.Entries
                .Where(entry => entry.IsEnabled && !string.IsNullOrWhiteSpace(entry.KeyCombination))
                .ToArray();
            var issues = new List<ShortcutRegistrationIssue>();

            var automaticEntries = entries
                .Where(entry => string.Equals(entry.ActionType, "AutomaticKey", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var globalEntries = entries
                .Where(entry => !string.Equals(entry.ActionType, "AutomaticKey", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var registeredCount = 0;

            if (automaticEntries.Length > 0)
            {
                var automaticAccess = await _platformAccess.EnsureAvailableAsync(
                    PlatformCapability.TextDelivery,
                    cancellationToken).ConfigureAwait(false);
                if (automaticAccess.IsFailure)
                {
                    issues.AddRange(automaticEntries.Select(entry => new ShortcutRegistrationIssue(
                        entry.ActionType,
                        entry.KeyCombination,
                        automaticAccess.Error)));
                }
                else
                {
                    var automaticIssues = await _automaticAction.ConfigureAsync(
                        automaticEntries,
                        cancellationToken).ConfigureAwait(false);
                    issues.AddRange(automaticIssues);
                }
            }

            if (globalEntries.Length == 0)
            {
                return new ShortcutRegistrationReport(entries.Length, registeredCount, issues);
            }

            var access = await _platformAccess.EnsureAvailableAsync(
                PlatformCapability.GlobalHotkeys,
                cancellationToken).ConfigureAwait(false);
            if (access.IsFailure)
            {
                issues.Add(new ShortcutRegistrationIssue(
                    "GlobalHotkeys",
                    string.Empty,
                    access.Error));
                return new ShortcutRegistrationReport(entries.Length, registeredCount, issues);
            }

            foreach (var entry in globalEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_actions.TryGetValue(entry.ActionType, out var action))
                {
                    issues.Add(new ShortcutRegistrationIssue(
                        entry.ActionType,
                        entry.KeyCombination,
                        new Error(
                            "shortcut.action-unavailable",
                            $"No shortcut action is registered for '{entry.ActionType}'.")));
                    continue;
                }

                var gesture = ShortcutGestureParser.Parse(entry.KeyCombination);
                if (gesture.IsFailure)
                {
                    issues.Add(new ShortcutRegistrationIssue(
                        entry.ActionType,
                        entry.KeyCombination,
                        gesture.Error));
                    continue;
                }

                var parameter = entry.Parameter;
                Result<IHotkeyRegistration> registration;
                if (action.Action is IHoldShortcutAction holdAction)
                {
                    if (_hotkeys is not IHoldGlobalHotkeys holdHotkeys)
                    {
                        issues.Add(new ShortcutRegistrationIssue(
                            entry.ActionType,
                            entry.KeyCombination,
                            new Error(
                                "hotkey.hold-unavailable",
                                "This platform does not support press-and-release hotkeys.")));
                        continue;
                    }

                    registration = await holdHotkeys.RegisterHoldAsync(
                        gesture.Value,
                        token => holdAction.ExecutePressedAsync(parameter, token),
                        token => holdAction.ExecuteReleasedAsync(parameter, token),
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    registration = await _hotkeys.RegisterAsync(
                        gesture.Value,
                        token => action.ExecuteAsync(parameter, token),
                        cancellationToken).ConfigureAwait(false);
                }
                if (registration.IsFailure)
                {
                    issues.Add(new ShortcutRegistrationIssue(
                        entry.ActionType,
                        entry.KeyCombination,
                        registration.Error));
                    continue;
                }

                _hotkeyRegistrations.Add(registration.Value);
                registeredCount++;
            }

            return new ShortcutRegistrationReport(
                entries.Length,
                registeredCount,
                issues);
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    public async ValueTask<Result> ProbeAsync(
        string keyCombination,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var access = await _platformAccess.EnsureAvailableAsync(
            PlatformCapability.GlobalHotkeys,
            cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
            return Result.Failure(access.Error);

        var gesture = ShortcutGestureParser.Parse(keyCombination);
        return gesture.IsFailure
            ? Result.Failure(gesture.Error)
            : await _hotkeys.ProbeAsync(gesture.Value, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (Interlocked.Exchange(ref _started, 0) != 0)
            _settings.SettingsChanged -= OnSettingsChanged;

        await _reloadLock.WaitAsync().ConfigureAwait(false);
        try
        {
            DisposeHotkeyRegistrations();
            await _automaticAction.ResetAsync().ConfigureAwait(false);
        }
        finally
        {
            _reloadLock.Release();
            _reloadLock.Dispose();
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs eventArgs)
    {
        if (eventArgs.Section == SettingsSection.Shortcut)
            _ = ReloadAfterSettingsChangeAsync();
    }

    private async Task ReloadAfterSettingsChangeAsync()
    {
        try
        {
            await ReloadAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void DisposeHotkeyRegistrations()
    {
        foreach (var registration in _hotkeyRegistrations)
            registration.Dispose();
        _hotkeyRegistrations.Clear();
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed class ActionState
    {
        private readonly IShortcutAction _action;
        private int _executing;

        public ActionState(IShortcutAction action) =>
            _action = action ?? throw new ArgumentNullException(nameof(action));

        public IShortcutAction Action => _action;

        public async ValueTask ExecuteAsync(
            ShortcutParameterSettings? parameter,
            CancellationToken cancellationToken)
        {
            if (_action.PreventConcurrentExecution &&
                Interlocked.CompareExchange(ref _executing, 1, 0) != 0)
            {
                return;
            }

            try
            {
                await _action.ExecuteAsync(parameter, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (_action.PreventConcurrentExecution)
                    Volatile.Write(ref _executing, 0);
            }
        }
    }
}
