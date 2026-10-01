using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Native;
using EasyChat.Shared.Results;
using Microsoft.Extensions.Logging;

namespace EasyChat.Infrastructure.MacOS.Input;

/// <summary>
/// Global shortcuts through Carbon hot keys.
/// </summary>
/// <remarks>
/// Registration is by virtual key code, which is a physical key position, so a shortcut survives an
/// input-source switch. Carbon hot keys also need no privacy approval and keep firing while EasyChat
/// is hidden or in the background, and they survive sleep and wake because the registration lives in
/// the window server rather than in an event stream this process has to hold open.
/// </remarks>
internal sealed class MacGlobalHotkeys(ILogger<MacGlobalHotkeys> logger) : IHoldGlobalHotkeys
{
    private static readonly ConcurrentDictionary<uint, Handlers> Registered = new();
    private static readonly Lock InstallGate = new();
    private static IntPtr _handlerRef;
    private static uint _nextId;

    public ValueTask<Result<IHotkeyRegistration>> RegisterAsync(
        ShortcutGesture gesture,
        Func<CancellationToken, ValueTask> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return ValueTask.FromResult(Register(gesture, callback, released: null, cancellationToken));
    }

    public ValueTask<Result<IHotkeyRegistration>> RegisterHoldAsync(
        ShortcutGesture gesture,
        Func<CancellationToken, ValueTask> pressed,
        Func<CancellationToken, ValueTask> released,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pressed);
        ArgumentNullException.ThrowIfNull(released);
        return ValueTask.FromResult(Register(gesture, pressed, released, cancellationToken));
    }

    /// <summary>
    /// Registers and immediately releases the shortcut, which is the only way macOS reports whether
    /// a combination is already taken.
    /// </summary>
    public ValueTask<Result> ProbeAsync(
        ShortcutGesture gesture,
        CancellationToken cancellationToken = default)
    {
        var probe = Register(gesture, _ => ValueTask.CompletedTask, null, cancellationToken);
        if (probe.IsFailure)
            return ValueTask.FromResult(Result.Failure(probe.Error));

        probe.Value.Dispose();
        return ValueTask.FromResult(Result.Success());
    }

    private Result<IHotkeyRegistration> Register(
        ShortcutGesture gesture,
        Func<CancellationToken, ValueTask> pressed,
        Func<CancellationToken, ValueTask>? released,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            return Result<IHotkeyRegistration>.Failure(new Error(
                "hotkey.unsupported",
                "Global shortcuts require macOS 26 or later."));
        }

        if (!MacKeyCombination.TryResolve(gesture, out var keystroke))
        {
            return Result<IHotkeyRegistration>.Failure(new Error(
                "hotkey.gesture-unsupported",
                $"'{gesture.Key}' is not a key macOS can register as a shortcut."));
        }

        var install = EnsureHandlerInstalled();
        if (install is { } failure)
            return Result<IHotkeyRegistration>.Failure(failure.Error);

        var id = Interlocked.Increment(ref _nextId);
        var hotKeyId = new EventHotKeyId { Signature = CarbonHotkeyNative.Signature, Id = id };
        Registered[id] = new Handlers(pressed, released);

        var status = CarbonHotkeyNative.RegisterEventHotKey(
            keystroke.VirtualKey,
            (uint)MacKeyCombination.ToCarbon(keystroke.Modifiers),
            hotKeyId,
            CarbonHotkeyNative.GetApplicationEventTarget(),
            options: 0,
            out var handle);
        if (status != 0 || handle == IntPtr.Zero)
        {
            Registered.TryRemove(id, out _);
            return Result<IHotkeyRegistration>.Failure(status == CarbonHotkeyNative.HotKeyExistsError
                ? new Error(
                    "hotkey.conflict",
                    $"Another application already owns the shortcut '{Describe(gesture)}'.")
                : new Error(
                    "hotkey.register-failed",
                    $"macOS refused to register '{Describe(gesture)}' (status {status})."));
        }

        return Result<IHotkeyRegistration>.Success(new Registration(id, handle, logger));
    }

    private Result? EnsureHandlerInstalled()
    {
        lock (InstallGate)
        {
            if (_handlerRef != IntPtr.Zero)
                return null;

            EventTypeSpec[] events =
            [
                new()
                {
                    EventClass = CarbonHotkeyNative.KeyboardEventClass,
                    EventKind = CarbonHotkeyNative.HotKeyPressed
                },
                new()
                {
                    EventClass = CarbonHotkeyNative.KeyboardEventClass,
                    EventKind = CarbonHotkeyNative.HotKeyReleased
                }
            ];

            var status = CarbonHotkeyNative.InstallEventHandler(
                CarbonHotkeyNative.GetApplicationEventTarget(),
                HandlerPointer,
                events.Length,
                events,
                IntPtr.Zero,
                out var handlerRef);
            if (status != 0)
            {
                return Result.Failure(new Error(
                    "hotkey.handler-failed",
                    $"The Carbon hot key handler could not be installed (status {status})."));
            }

            _handlerRef = handlerRef;
            logger.LogDebug("Installed the Carbon hot key handler.");
            return null;
        }
    }

    private static unsafe IntPtr HandlerPointer =>
        (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, int>)&OnHotKeyEvent;

    /// <summary>
    /// Carbon dispatches this on the main thread's run loop, so it must return at once: the managed
    /// work is handed to the thread pool rather than run here, or a slow translation would stall
    /// every other event the application is waiting on.
    /// </summary>
    [UnmanagedCallersOnly]
    private static int OnHotKeyEvent(IntPtr callRef, IntPtr handle, IntPtr userData)
    {
        if (!CarbonHotkeyNative.TryReadHotKeyId(handle, out var hotKeyId)
            || hotKeyId.Signature != CarbonHotkeyNative.Signature
            || !Registered.TryGetValue(hotKeyId.Id, out var handlers))
        {
            // Not ours: let the next handler in the chain see it.
            return EventNotHandled;
        }

        var kind = CarbonHotkeyNative.GetEventKind(handle);
        var callback = kind == CarbonHotkeyNative.HotKeyReleased ? handlers.Released : handlers.Pressed;
        if (callback is null)
            return EventNotHandled;

        _ = Task.Run(async () =>
        {
            try
            {
                await callback(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // A failing action must never tear down the hot key handler for every other one.
            }
        });
        return 0;
    }

    /// <summary>Value of <c>eventNotHandledErr</c>.</summary>
    private const int EventNotHandled = -9874;

    private static string Describe(ShortcutGesture gesture) =>
        gesture.Modifiers == ShortcutModifiers.None
            ? gesture.Key
            : $"{gesture.Modifiers} + {gesture.Key}";

    private sealed record Handlers(
        Func<CancellationToken, ValueTask> Pressed,
        Func<CancellationToken, ValueTask>? Released);

    private sealed class Registration(uint id, IntPtr handle, ILogger logger) : IHotkeyRegistration
    {
        private IntPtr _handle = handle;

        public void Dispose()
        {
            var owned = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            if (owned == IntPtr.Zero)
                return;

            Registered.TryRemove(id, out _);
            var status = CarbonHotkeyNative.UnregisterEventHotKey(owned);
            if (status != 0)
                logger.LogWarning("Unregistering a hot key failed with status {Status}.", status);
        }
    }
}
