using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using EasyChat.Desktop.ApplicationLifecycle;

namespace EasyChat.Desktop.MacOS.ApplicationLifecycle;

internal sealed class MacOSDesktopInstanceCoordinator : IDesktopInstanceCoordinator
{
    public IDesktopInstanceLease? AcquireOrSignal(bool waitForExistingRelease = false)
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
            throw new PlatformNotSupportedException("EasyChat requires macOS 26 or later.");

        return MacOSDesktopInstanceLease.AcquireOrSignal(waitForExistingRelease);
    }
}

internal sealed class MacOSDesktopInstanceLease : IDesktopInstanceLease
{
    private const int ActivationSignalAttempts = 100;
    private const int ActivationSignalDelayMilliseconds = 25;
    private static readonly TimeSpan RestartWaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly string StateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EasyChat");
    private static readonly string LockPath = Path.Combine(StateDirectory, "instance.lock");
    private static readonly string SocketPath = CreateSocketPath();

    private readonly FileStream _lock;
    private readonly Socket _listener;
    private readonly Thread _thread;
    private readonly object _gate = new();
    private Action? _activationHandler;
    private bool _activationPending;
    private bool _disposed;

    private MacOSDesktopInstanceLease(FileStream instanceLock, Socket listener)
    {
        _lock = instanceLock;
        _listener = listener;
        _thread = new Thread(ListenForActivation)
        {
            IsBackground = true,
            Name = "EasyChat macOS single-instance listener"
        };
        _thread.Start();
    }

    public static MacOSDesktopInstanceLease? AcquireOrSignal(bool waitForExistingRelease)
    {
        Directory.CreateDirectory(StateDirectory);
        var deadline = DateTime.UtcNow + (waitForExistingRelease ? RestartWaitTimeout : TimeSpan.Zero);
        do
        {
            try
            {
                var instanceLock = new FileStream(
                    LockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
                return CreateOwner(instanceLock);
            }
            catch (IOException) when (waitForExistingRelease && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(ActivationSignalDelayMilliseconds);
            }
            catch (IOException)
            {
                SignalActivation();
                return null;
            }
        } while (true);
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

        _listener.Dispose();
        if (!ReferenceEquals(Thread.CurrentThread, _thread))
            _thread.Join(TimeSpan.FromSeconds(1));
        TryDeleteSocket();
        _lock.Dispose();
    }

    private static MacOSDesktopInstanceLease CreateOwner(FileStream instanceLock)
    {
        Socket? listener = null;
        try
        {
            TryDeleteSocket();
            listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
            listener.Listen(4);
            return new MacOSDesktopInstanceLease(instanceLock, listener);
        }
        catch
        {
            listener?.Dispose();
            instanceLock.Dispose();
            TryDeleteSocket();
            throw;
        }
    }

    private static void SignalActivation()
    {
        for (var attempt = 0; attempt < ActivationSignalAttempts; attempt++)
        {
            try
            {
                using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(new UnixDomainSocketEndPoint(SocketPath));
                socket.Send([1]);
                return;
            }
            catch (SocketException)
            {
                Thread.Sleep(ActivationSignalDelayMilliseconds);
            }
        }
    }

    private void ListenForActivation()
    {
        var signal = new byte[1];
        while (true)
        {
            try
            {
                using var connection = _listener.Accept();
                if (connection.Receive(signal) == 0)
                    continue;

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
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                lock (_gate)
                {
                    if (_disposed)
                        return;
                }
            }
        }
    }

    private static string CreateSocketPath()
    {
        var identity = Encoding.UTF8.GetBytes(
            Environment.UserName + "\n" + StateDirectory);
        var suffix = Convert.ToHexString(SHA256.HashData(identity))[..12].ToLowerInvariant();
        return Path.Combine(Path.GetTempPath(), $"easychat-{suffix}.sock");
    }

    private static void TryDeleteSocket()
    {
        try
        {
            File.Delete(SocketPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
