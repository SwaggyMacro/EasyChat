using EasyChat.Desktop.ApplicationLifecycle;

namespace EasyChat.Desktop.Windows.ApplicationLifecycle;

internal sealed class WindowsDesktopInstanceCoordinator : IDesktopInstanceCoordinator
{
    public IDesktopInstanceLease? AcquireOrSignal(bool waitForExistingRelease = false) =>
        WindowsDesktopInstanceLease.AcquireOrSignal(waitForExistingRelease);
}

internal sealed class WindowsDesktopInstanceLease : IDesktopInstanceLease
{
    private const string MutexName = @"Local\EasyChat.Desktop.SingleInstance";
    private const string ActivationEventName = @"Local\EasyChat.Desktop.Activate";
    private const int ActivationSignalAttempts = 100;
    private const int ActivationSignalDelayMilliseconds = 25;
    private static readonly TimeSpan RestartWaitTimeout = TimeSpan.FromSeconds(30);

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly Thread _listener;
    private readonly object _gate = new();
    private Action? _activationHandler;
    private bool _activationPending;
    private bool _disposed;

    private WindowsDesktopInstanceLease(Mutex mutex, EventWaitHandle activationEvent)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
        _listener = new Thread(ListenForActivation)
        {
            IsBackground = true,
            Name = "EasyChat Windows single-instance listener"
        };
        _listener.Start();
    }

    public static WindowsDesktopInstanceLease? AcquireOrSignal(bool waitForExistingRelease)
    {
        var mutex = new Mutex(true, MutexName, out var createdNew);
        var ownsMutex = createdNew;
        if (!createdNew)
        {
            try
            {
                ownsMutex = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }
        }

        if (!ownsMutex)
        {
            if (waitForExistingRelease)
            {
                try
                {
                    ownsMutex = mutex.WaitOne(RestartWaitTimeout);
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }
            }

            if (ownsMutex)
                return CreateOwner(mutex);

            SignalActivation();
            mutex.Dispose();
            return null;
        }

        return CreateOwner(mutex);
    }

    public void SetActivationHandler(Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var invokePending = false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activationHandler = handler;
            if (_activationPending)
            {
                _activationPending = false;
                invokePending = true;
            }
        }

        if (invokePending)
            handler();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _activationHandler = null;
            _activationPending = false;
        }

        try
        {
            _activationEvent.Set();
        }
        catch (ObjectDisposedException)
        {
        }

        if (!ReferenceEquals(Thread.CurrentThread, _listener))
            _listener.Join(TimeSpan.FromSeconds(1));

        _activationEvent.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        _mutex.Dispose();
    }

    private static WindowsDesktopInstanceLease CreateOwner(Mutex mutex)
    {
        try
        {
            var activationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                ActivationEventName);
            return new WindowsDesktopInstanceLease(mutex, activationEvent);
        }
        catch
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            mutex.Dispose();
            throw;
        }
    }

    private static void SignalActivation()
    {
        for (var attempt = 0; attempt < ActivationSignalAttempts; attempt++)
        {
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
                activationEvent.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(ActivationSignalDelayMilliseconds);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    private void ListenForActivation()
    {
        try
        {
            while (true)
            {
                _activationEvent.WaitOne();

                Action? handler;
                lock (_gate)
                {
                    if (_disposed)
                        return;
                    handler = _activationHandler;
                    if (handler is null)
                        _activationPending = true;
                }

                try
                {
                    handler?.Invoke();
                }
                catch
                {
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
