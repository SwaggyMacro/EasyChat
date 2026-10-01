using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using EasyChat.Contracts.Capture;
using EasyChat.Presentation.Features.Capture;

namespace EasyChat.Desktop.MacOS.Capture;

/// <summary>
/// Runs the selection overlay and the screen capture in a short-lived helper process.
/// </summary>
/// <remarks>
/// <para>
/// The isolation is about memory, not only crashes: a full-desktop Retina frame is around twenty
/// megabytes and a long screenshot is a multiple of that, and letting the helper exit hands all of
/// it straight back to the system instead of leaving it in the main process's heap for the rest of
/// the session.
/// </para>
/// <para>
/// The transport is a Unix domain socket rather than a named pipe, and its path is kept short
/// because the platform caps a socket path at just over a hundred bytes.
/// </para>
/// </remarks>
internal sealed class MacScreenshotCaptureSession : IScreenshotCaptureSession, IDisposable
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private Socket? _listener;
    private Socket? _connection;
    private NetworkStream? _stream;
    private BinaryReader? _reader;
    private BinaryWriter? _writer;
    private string? _socketPath;
    private bool _disposed;

    public async ValueTask WarmUpAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await EnsureWorkerAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ScreenshotSelection?> CaptureAsync(
        bool precise,
        CaptureOverlayAction defaultAction,
        CaptureToolbarMode toolbarMode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var appearance = await ReadAppearanceAsync(cancellationToken).ConfigureAwait(false);
        var request = new ScreenshotWorkerRequest(
            precise,
            appearance.Theme,
            appearance.PrimaryColor,
            CultureInfo.CurrentUICulture.Name,
            defaultAction,
            toolbarMode);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // A helper can be gone while the application sits idle, so one rebuild is attempted
            // before giving up: the user's first hotkey press should still be the one that captures.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await EnsureWorkerAsync(cancellationToken).ConfigureAwait(false);
                    var process = _process
                                  ?? throw new InvalidOperationException(
                                      "The screenshot worker was not initialised.");
                    await using var registration = cancellationToken.Register(
                        static state => TryTerminate((Process)state!),
                        process);
                    var writer = _writer!;
                    var reader = _reader!;
                    var selection = await Task.Run(
                        () =>
                        {
                            ScreenshotWorkerProtocol.WriteRequest(writer, request);
                            return ScreenshotWorkerProtocol.Read(reader);
                        },
                        CancellationToken.None).ConfigureAwait(false);

                    // The helper has answered, so it is retired now rather than kept warm: holding
                    // it would keep the very buffers this design exists to release.
                    ResetWorker();
                    return selection;
                }
                catch (Exception exception) when (IsRecoverableWorkerFailure(exception) && attempt == 0)
                {
                    ResetWorker();
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            ResetWorker();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task EnsureWorkerAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false } && _connection?.Connected == true)
            return;

        ResetWorker();
        var socketPath = CreateSocketPath();
        Socket? listener = null;
        Process? process = null;
        Socket? connection = null;
        NetworkStream? stream = null;
        BinaryReader? reader = null;
        BinaryWriter? writer = null;
        try
        {
            listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            listener.Listen(1);

            process = StartWorker(socketPath);
            await using var registration = cancellationToken.Register(
                static state => TryTerminate((Process)state!),
                process);
            using var connecting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connecting.CancelAfter(ConnectionTimeout);
            try
            {
                connection = await listener.AcceptAsync(connecting.Token).ConfigureAwait(false);
                stream = new NetworkStream(connection, ownsSocket: false);
                reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
                writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
                await Task.Run(
                        () => ScreenshotWorkerProtocol.ReadReady(reader),
                        CancellationToken.None)
                    .WaitAsync(connecting.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The screenshot worker did not become ready in time.");
            }

            _listener = listener;
            _connection = connection;
            _stream = stream;
            _process = process;
            _reader = reader;
            _writer = writer;
            _socketPath = socketPath;
            listener = null;
            connection = null;
            stream = null;
            process = null;
            reader = null;
            writer = null;
        }
        finally
        {
            writer?.Dispose();
            reader?.Dispose();
            stream?.Dispose();
            connection?.Dispose();
            listener?.Dispose();
            if (process is not null)
            {
                TryTerminate(process);
                TryWaitForExit(process, milliseconds: 5000);
                process.Dispose();
            }

            if (listener is not null || process is not null)
                TryDeleteSocket(socketPath);
        }
    }

    private static async Task<ScreenshotAppearance> ReadAppearanceAsync(
        CancellationToken cancellationToken)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return ReadAppearance();
        return await Dispatcher.UIThread.InvokeAsync(
            ReadAppearance,
            DispatcherPriority.Normal,
            cancellationToken);
    }

    private static ScreenshotAppearance ReadAppearance()
    {
        var application = Avalonia.Application.Current;
        var theme = application?.ActualThemeVariant == ThemeVariant.Dark
            ? "Dark"
            : application?.ActualThemeVariant == ThemeVariant.Light
                ? "Light"
                : "Default";
        var primaryColor = application?.TryGetResource(
                "PrimaryColor",
                application.ActualThemeVariant,
                out var resource) == true
            ? resource switch
            {
                Color color => color.ToString(),
                ISolidColorBrush brush => brush.Color.ToString(),
                _ => string.Empty
            }
            : string.Empty;
        return new ScreenshotAppearance(theme, primaryColor);
    }

    private static Process StartWorker(string socketPath)
    {
        var executable = Environment.ProcessPath
                         ?? throw new InvalidOperationException(
                             "Unable to locate the EasyChat executable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--screenshot-worker");
        startInfo.ArgumentList.Add(socketPath);
        return Process.Start(startInfo)
               ?? throw new InvalidOperationException(
                   "Unable to start the screenshot worker process.");
    }

    private void ResetWorker()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _stream?.Dispose();
        _connection?.Dispose();
        _listener?.Dispose();
        _writer = null;
        _reader = null;
        _stream = null;
        _connection = null;
        _listener = null;
        if (_socketPath is { } path)
        {
            TryDeleteSocket(path);
            _socketPath = null;
        }

        if (_process is not { } process)
            return;
        _process = null;
        TryTerminate(process);
        if (!TryWaitForExit(process, milliseconds: 500))
        {
            TryTerminate(process);
            TryWaitForExit(process, milliseconds: 5000);
        }

        process.Dispose();
    }

    /// <summary>
    /// A short, per-request path under the temporary directory. The platform limits a Unix socket
    /// path to just over a hundred bytes, which a full application-support path can exceed.
    /// </summary>
    private static string CreateSocketPath()
    {
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        return Path.Combine(Path.GetTempPath(), $"easychat-shot-{suffix}.sock");
    }

    private static void TryDeleteSocket(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsRecoverableWorkerFailure(Exception exception) =>
        exception is IOException
            or EndOfStreamException
            or InvalidDataException
            or ObjectDisposedException
            or TimeoutException
            or SocketException
            or InvalidOperationException;

    private static bool TryWaitForExit(Process process, int milliseconds)
    {
        try
        {
            return process.HasExited || process.WaitForExit(milliseconds);
        }
        catch
        {
            return true;
        }
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Worker cleanup is best effort.
        }
    }

    private readonly record struct ScreenshotAppearance(string Theme, string PrimaryColor);
}
