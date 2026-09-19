using EasyChat.Application.Shortcuts;
using EasyChat.Application.Tests.Settings;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Shortcuts;
using EasyChat.Shared.Results;

namespace EasyChat.Application.Tests.Shortcuts;

[TestClass]
public sealed class ShortcutCoordinatorTests
{
    [TestMethod]
    public async Task StartAsync_RegistersEnabledKnownActionsAndOwnsTheirLifetime()
    {
        var bundle = SettingsTestData.CreateBundle() with
        {
            Shortcut = new ShortcutSettings(
            [
                new ShortcutEntrySettings("Screenshot", null, "Ctrl + Oem3", true),
                new ShortcutEntrySettings("Screenshot", null, "Alt + F8", false),
                new ShortcutEntrySettings("Unknown", null, "Ctrl + U", true)
            ])
        };
        var hotkeys = new FakeGlobalHotkeys();
        var action = new FakeShortcutAction();
        await using var coordinator = new ShortcutCoordinator(
            new FakeSettingsUseCases(bundle),
            new AvailablePlatformAccess(),
            hotkeys,
            [action],
            new FakeAutomaticShortcutAction());

        var report = await coordinator.StartAsync();

        Assert.AreEqual(2, report.RequestedCount);
        Assert.AreEqual(1, report.RegisteredCount);
        Assert.HasCount(1, report.Issues);
        Assert.AreEqual("shortcut.action-unavailable", report.Issues[0].Error.Code);
        Assert.AreEqual(
            new ShortcutGesture("Oem3", ShortcutModifiers.Control),
            hotkeys.Gesture);

        await hotkeys.Callback!(CancellationToken.None);
        Assert.AreEqual(1, action.ExecutionCount);
        await coordinator.DisposeAsync();
        Assert.AreEqual(1, hotkeys.Registration.DisposeCount);
    }

    [TestMethod]
    public async Task StartAsync_RegistersHoldActionsWithPressAndReleaseCallbacks()
    {
        var bundle = SettingsTestData.CreateBundle() with
        {
            Shortcut = new ShortcutSettings(
            [
                new ShortcutEntrySettings("RealtimeInterpretation", null, "Ctrl + Space", true)
            ])
        };
        var hotkeys = new FakeHoldGlobalHotkeys();
        var action = new FakeHoldShortcutAction();
        await using var coordinator = new ShortcutCoordinator(
            new FakeSettingsUseCases(bundle),
            new AvailablePlatformAccess(),
            hotkeys,
            [action],
            new FakeAutomaticShortcutAction());

        var report = await coordinator.StartAsync();

        Assert.AreEqual(1, report.RegisteredCount);
        Assert.IsNotNull(hotkeys.Pressed);
        Assert.IsNotNull(hotkeys.Released);
        await hotkeys.Pressed(CancellationToken.None);
        await hotkeys.Released(CancellationToken.None);
        Assert.AreEqual(1, action.PressedCount);
        Assert.AreEqual(1, action.ReleasedCount);
        Assert.AreEqual(0, action.ExecutionCount);
    }

    [TestMethod]
    public async Task RegisteredCallback_SkipsConcurrentExecutionWhenActionRequiresIt()
    {
        var bundle = SettingsTestData.CreateBundle() with
        {
            Shortcut = new ShortcutSettings(
            [
                new ShortcutEntrySettings("Screenshot", null, "Ctrl + Oem3", true)
            ])
        };
        var hotkeys = new FakeGlobalHotkeys();
        var action = new BlockingShortcutAction();
        await using var coordinator = new ShortcutCoordinator(
            new FakeSettingsUseCases(bundle),
            new AvailablePlatformAccess(),
            hotkeys,
            [action],
            new FakeAutomaticShortcutAction());

        await coordinator.StartAsync();

        var firstExecution = hotkeys.Callback!(CancellationToken.None).AsTask();
        await action.Started.Task;
        var secondExecution = hotkeys.Callback(CancellationToken.None).AsTask();

        action.Complete();
        await Task.WhenAll(firstExecution, secondExecution);

        Assert.AreEqual(1, action.ExecutionCount);
    }

    [TestMethod]
    public async Task StartAsync_ConfiguresAutomaticKeysWithoutRegisteringGlobalHotkeys()
    {
        var bundle = SettingsTestData.CreateBundle() with
        {
            Shortcut = new ShortcutSettings(
            [
                new ShortcutEntrySettings(
                    "AutomaticKey",
                    new ShortcutParameterSettings(
                        string.Empty,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        AutomaticShortcutTrigger.RealtimeInterpretationPlayback,
                        AutomaticShortcutPressMode.Hold),
                    "V",
                    true)
            ])
        };
        var hotkeys = new FakeGlobalHotkeys();
        var automaticAction = new FakeAutomaticShortcutAction();
        await using var coordinator = new ShortcutCoordinator(
            new FakeSettingsUseCases(bundle),
            new AvailablePlatformAccess(),
            hotkeys,
            [],
            automaticAction);

        var report = await coordinator.StartAsync();

        Assert.AreEqual(1, report.RequestedCount);
        Assert.AreEqual(0, report.RegisteredCount);
        Assert.IsEmpty(report.Issues);
        Assert.AreEqual(1, automaticAction.ConfigureCount);
        Assert.AreEqual(0, hotkeys.RegisterCount);
    }

    private sealed class FakeShortcutAction : IShortcutAction
    {
        public string ActionType => "Screenshot";
        public bool PreventConcurrentExecution => true;
        public int ExecutionCount { get; private set; }

        public ValueTask ExecuteAsync(
            ShortcutParameterSettings? parameter,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeHoldShortcutAction : IHoldShortcutAction
    {
        public string ActionType => "RealtimeInterpretation";
        public bool PreventConcurrentExecution => false;
        public int ExecutionCount { get; private set; }
        public int PressedCount { get; private set; }
        public int ReleasedCount { get; private set; }

        public ValueTask ExecuteAsync(
            ShortcutParameterSettings? parameter,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ExecutePressedAsync(
            ShortcutParameterSettings? parameter,
            CancellationToken cancellationToken = default)
        {
            PressedCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ExecuteReleasedAsync(
            ShortcutParameterSettings? parameter,
            CancellationToken cancellationToken = default)
        {
            ReleasedCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingShortcutAction : IShortcutAction
    {
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public string ActionType => "Screenshot";
        public bool PreventConcurrentExecution => true;
        public int ExecutionCount { get; private set; }
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ExecuteAsync(
            ShortcutParameterSettings? parameter,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            Started.TrySetResult();
            await _completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete() => _completion.TrySetResult();
    }

    private sealed class FakeAutomaticShortcutAction : IAutomaticShortcutAction
    {
        public int ConfigureCount { get; private set; }

        public ValueTask<IReadOnlyList<ShortcutRegistrationIssue>> ConfigureAsync(
            IReadOnlyList<ShortcutEntrySettings> entries,
            CancellationToken cancellationToken = default)
        {
            ConfigureCount++;
            return ValueTask.FromResult<IReadOnlyList<ShortcutRegistrationIssue>>([]);
        }

        public ValueTask ResetAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class FakeGlobalHotkeys : IGlobalHotkeys
    {
        public ShortcutGesture? Gesture { get; private set; }
        public Func<CancellationToken, ValueTask>? Callback { get; private set; }
        public FakeRegistration Registration { get; } = new();
        public int RegisterCount { get; private set; }

        public ValueTask<Result<IHotkeyRegistration>> RegisterAsync(
            ShortcutGesture gesture,
            Func<CancellationToken, ValueTask> callback,
            CancellationToken cancellationToken = default)
        {
            RegisterCount++;
            Gesture = gesture;
            Callback = callback;
            return ValueTask.FromResult(Result<IHotkeyRegistration>.Success(Registration));
        }

        public ValueTask<Result> ProbeAsync(
            ShortcutGesture gesture,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success());
    }

    private sealed class FakeHoldGlobalHotkeys : IHoldGlobalHotkeys
    {
        public Func<CancellationToken, ValueTask>? Pressed { get; private set; }
        public Func<CancellationToken, ValueTask>? Released { get; private set; }
        public FakeRegistration Registration { get; } = new();

        public ValueTask<Result<IHotkeyRegistration>> RegisterAsync(
            ShortcutGesture gesture,
            Func<CancellationToken, ValueTask> callback,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("Hold action must not use the one-shot registration path.");

        public ValueTask<Result<IHotkeyRegistration>> RegisterHoldAsync(
            ShortcutGesture gesture,
            Func<CancellationToken, ValueTask> pressed,
            Func<CancellationToken, ValueTask> released,
            CancellationToken cancellationToken = default)
        {
            Pressed = pressed;
            Released = released;
            return ValueTask.FromResult(Result<IHotkeyRegistration>.Success(Registration));
        }

        public ValueTask<Result> ProbeAsync(
            ShortcutGesture gesture,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success());
    }

    private sealed class FakeRegistration : IHotkeyRegistration
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    private sealed class FakeSettingsUseCases(SettingsBundle current) : ISettingsUseCases
    {
        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<SettingsSaveFailedEventArgs>? SaveFailed
        {
            add { }
            remove { }
        }

        public bool IsInitialized => true;
        public SettingsBundle Current { get; } = current;

        public ValueTask<Result<SettingsBundle>> InitializeAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<SettingsBundle>.Success(Current));

        public Result Update(SettingsSection section, SettingsBundle settings) => Result.Success();
        public ValueTask<Result> FlushAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
