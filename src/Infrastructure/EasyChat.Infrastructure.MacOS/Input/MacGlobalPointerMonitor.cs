using System.Runtime.InteropServices;
using System.Threading.Channels;
using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Capture;
using EasyChat.Infrastructure.MacOS.Native;
using Microsoft.Extensions.Logging;

namespace EasyChat.Infrastructure.MacOS.Input;

/// <summary>
/// Watches primary mouse clicks across the desktop so the selection workflow knows when a drag that
/// might have selected text has finished.
/// </summary>
/// <remarks>
/// The tap is listen-only, so EasyChat can never alter or swallow a click: a fault here cannot make
/// the user's mouse stop working. It runs on a thread of its own with its own run loop, because a
/// tap has to be serviced by a run loop and borrowing the UI one would couple mouse latency to
/// whatever the interface is doing. The tap callback itself only timestamps the click and hands it
/// to a channel; resolving the foreground application, the window under the pointer and the
/// pasteboard state all happen on the pump, since anything slow inside the callback shows up as
/// system-wide input lag.
/// </remarks>
internal sealed class MacGlobalPointerMonitor(ILogger<MacGlobalPointerMonitor> logger)
    : IGlobalPointerMonitor
{
    private readonly Lock _gate = new();
    private readonly Dictionary<long, Action<GlobalPointerEvent>> _callbacks = [];
    private TapSession? _session;
    private long _nextRegistration;

    public IPointerMonitorRegistration Start(Action<GlobalPointerEvent> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        long id;
        lock (_gate)
        {
            id = ++_nextRegistration;
            _callbacks[id] = callback;
            if (_session is null)
            {
                _session = TapSession.TryStart(Publish, logger);
                if (_session is null)
                {
                    // Without Accessibility or Input Monitoring approval macOS refuses the tap. The
                    // registration is still handed back so the caller's lifetime stays symmetric,
                    // but no event will arrive and the log says why.
                    logger.LogWarning(
                        "The pointer event tap could not be created; macOS has not granted input monitoring.");
                }
            }
        }

        return new Registration(this, id);
    }

    private void Release(long id)
    {
        TapSession? closing = null;
        lock (_gate)
        {
            if (!_callbacks.Remove(id) || _callbacks.Count != 0)
                return;

            closing = _session;
            _session = null;
        }

        closing?.Dispose();
    }

    /// <summary>
    /// Turns a raw click into the contract event. This runs on the pump thread, never on the tap.
    /// </summary>
    private void Publish(RawClick click)
    {
        Action<GlobalPointerEvent>[] callbacks;
        lock (_gate)
            callbacks = [.. _callbacks.Values];
        if (callbacks.Length == 0)
            return;

        var displays = MacDisplayGeometry.GetDisplays();
        var position = MacDisplayGeometry.ToPhysicalPoint(click.Location, displays);
        var windows = WindowListNative.OnScreen();
        var underPointer = TopmostAt(click.Location, windows);

        using var pool = AutoreleasePool.Push();
        var foreground = MacTargetTokens.FromTarget(new MacTarget(
            WorkspaceNative.ProcessIdentifier(WorkspaceNative.FrontmostApplication()),
            0));
        var pointerTarget = underPointer is { } window
            ? MacTargetTokens.FromTarget(
                new MacTarget(window.OwnerProcessIdentifier, window.WindowNumber))
            : ExternalTargetToken.None;

        var pointerEvent = new GlobalPointerEvent(
            click.Action,
            position,
            click.Timestamp,
            foreground,
            unchecked((uint)PasteboardNative.ChangeCount()),
            pointerTarget,
            // macOS has no mouse-capture window, so this stays empty and the workflow's
            // capture-change guard disables itself rather than comparing invented values.
            CapturedTarget: ExternalTargetToken.None,
            PointerTargetIsOverlay: underPointer?.OwnerProcessIdentifier == Environment.ProcessId);

        foreach (var callback in callbacks)
        {
            try
            {
                callback(pointerEvent);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "A pointer monitor callback failed.");
            }
        }
    }

    private static WindowListEntry? TopmostAt(
        CoreGraphicsPoint point,
        IReadOnlyList<WindowListEntry> windows)
    {
        foreach (var window in windows)
        {
            if (point.X >= window.Bounds.X
                && point.X < window.Bounds.X + window.Bounds.Width
                && point.Y >= window.Bounds.Y
                && point.Y < window.Bounds.Y + window.Bounds.Height)
            {
                return window;
            }
        }

        return null;
    }

    private sealed record RawClick(
        PointerAction Action,
        CoreGraphicsPoint Location,
        DateTimeOffset Timestamp);

    private sealed class Registration(MacGlobalPointerMonitor monitor, long id)
        : IPointerMonitorRegistration
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                monitor.Release(id);
        }
    }

    /// <summary>Owns the tap, its dedicated run loop thread, and the pump that drains the channel.</summary>
    private sealed class TapSession : IDisposable
    {
        private static readonly Dictionary<IntPtr, TapSession> Sessions = [];
        private static readonly Lock SessionGate = new();
        private static IntPtr _nextKey = 1;

        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(2);

        private readonly Channel<RawClick> _clicks =
            Channel.CreateUnbounded<RawClick>(new UnboundedChannelOptions { SingleReader = true });

        private readonly ILogger _logger;
        private readonly IntPtr _key;
        private readonly Action<RawClick> _publish;
        private readonly Thread _runLoopThread;
        private readonly Task _pump;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly TaskCompletionSource<bool> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private IntPtr _tap;
        private IntPtr _source;
        private IntPtr _runLoop;

        private TapSession(Action<RawClick> publish, ILogger logger, IntPtr key)
        {
            _publish = publish;
            _logger = logger;
            _key = key;
            _pump = Task.Run(PumpAsync);
            _runLoopThread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = "EasyChat macOS pointer event tap"
            };
            _runLoopThread.Start();
        }

        internal static TapSession? TryStart(Action<RawClick> publish, ILogger logger)
        {
            if (!OperatingSystem.IsMacOSVersionAtLeast(26))
                return null;

            TapSession session;
            lock (SessionGate)
            {
                var key = _nextKey;
                _nextKey += 1;
                session = new TapSession(publish, logger, key);
                Sessions[key] = session;
            }

            // Bounded: the run loop thread answers before it starts pumping, but a caller on the
            // UI path must never be blocked indefinitely by a native call that did not come back.
            if (session._started.Task.Wait(StartTimeout) && session._started.Task.Result)
                return session;

            session.Dispose();
            return null;
        }

        public void Dispose()
        {
            _lifetime.Cancel();
            var runLoop = Interlocked.Exchange(ref _runLoop, IntPtr.Zero);
            if (runLoop != IntPtr.Zero)
                CoreFoundationNative.CFRunLoopStop(runLoop);

            // The run loop thread owns every CoreFoundation handle it created, so it is joined
            // before anything is released: no background thread may outlive this call.
            if (!ReferenceEquals(Thread.CurrentThread, _runLoopThread))
                _runLoopThread.Join(TimeSpan.FromSeconds(2));

            _clicks.Writer.TryComplete();
            try
            {
                _pump.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }

            lock (SessionGate)
                Sessions.Remove(_key);
            _lifetime.Dispose();
        }

        private void RunLoop()
        {
            try
            {
                _tap = EventTapNative.CreateMouseTap(CallbackPointer, _key);
                if (_tap == IntPtr.Zero)
                {
                    _started.TrySetResult(false);
                    return;
                }

                _source = CoreFoundationNative.CFMachPortCreateRunLoopSource(
                    IntPtr.Zero,
                    _tap,
                    0);
                if (_source == IntPtr.Zero)
                {
                    _started.TrySetResult(false);
                    return;
                }

                _runLoop = CoreFoundationNative.CFRunLoopGetCurrent();
                CoreFoundationNative.CFRunLoopAddSource(
                    _runLoop,
                    _source,
                    CoreFoundationNative.RunLoopCommonModes);
                EventTapNative.CGEventTapEnable(_tap, true);
                _started.TrySetResult(true);

                CoreFoundationNative.CFRunLoopRun();
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "The pointer event tap thread failed.");
                _started.TrySetResult(false);
            }
            finally
            {
                ReleaseNative();
            }
        }

        private void ReleaseNative()
        {
            var runLoop = _runLoop;
            var source = Interlocked.Exchange(ref _source, IntPtr.Zero);
            if (source != IntPtr.Zero)
            {
                if (runLoop != IntPtr.Zero)
                {
                    CoreFoundationNative.CFRunLoopRemoveSource(
                        runLoop,
                        source,
                        CoreFoundationNative.RunLoopCommonModes);
                }

                CoreFoundationNative.CFRelease(source);
            }

            var tap = Interlocked.Exchange(ref _tap, IntPtr.Zero);
            if (tap != IntPtr.Zero)
            {
                EventTapNative.CGEventTapEnable(tap, false);
                CoreFoundationNative.CFRelease(tap);
            }

            _runLoop = IntPtr.Zero;
        }

        private async Task PumpAsync()
        {
            try
            {
                await foreach (var click in _clicks.Reader.ReadAllAsync(_lifetime.Token)
                                   .ConfigureAwait(false))
                {
                    _publish(click);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "The pointer event pump failed.");
            }
        }

        private void Enqueue(uint type, IntPtr handle)
        {
            if (type is EventTapType.DisabledByTimeout or EventTapType.DisabledByUserInput)
            {
                // macOS switches a tap off when it is too slow or when the user forces it off.
                // Re-enabling is the documented recovery; without it the monitor dies silently.
                _logger.LogWarning(
                    "The pointer event tap was disabled by macOS (reason {Reason}); re-enabling.",
                    type == EventTapType.DisabledByTimeout ? "timeout" : "user input");
                EventTapNative.CGEventTapEnable(_tap, true);
                return;
            }

            var action = type switch
            {
                EventTapType.LeftMouseDown => EventTapNative.ClickCount(handle) >= 2
                    ? PointerAction.PrimaryDoubleClick
                    : PointerAction.PrimaryPressed,
                EventTapType.LeftMouseUp => PointerAction.PrimaryReleased,
                _ => (PointerAction?)null
            };
            if (action is not { } resolved)
                return;

            _clicks.Writer.TryWrite(new RawClick(
                resolved,
                DisplayNative.CGEventGetLocation(handle),
                DateTimeOffset.UtcNow));
        }

        private static unsafe IntPtr CallbackPointer =>
            (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&OnEvent;

        /// <summary>
        /// Runs on the tap's run loop for every click on the desktop, so it does the least possible
        /// work and always returns the event untouched.
        /// </summary>
        [UnmanagedCallersOnly]
        private static IntPtr OnEvent(IntPtr proxy, uint type, IntPtr handle, IntPtr userInfo)
        {
            TapSession? session;
            lock (SessionGate)
                Sessions.TryGetValue(userInfo, out session);

            try
            {
                session?.Enqueue(type, handle);
            }
            catch
            {
                // Never let a managed fault escape into CoreFoundation's callback frame.
            }

            return handle;
        }
    }
}
