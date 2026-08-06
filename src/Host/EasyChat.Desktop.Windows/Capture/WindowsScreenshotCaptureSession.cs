using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.Versioning;
using Avalonia.Styling;
using Avalonia.Threading;
using EasyChat.Contracts.Capture;
using EasyChat.Presentation.Features.Capture;

namespace EasyChat.Desktop.Windows.Capture;

[SupportedOSPlatform("windows")]
internal sealed class WindowsScreenshotCaptureSession : IScreenshotCaptureSession, IDisposable
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private NamedPipeServerStream? _pipe;
    private BinaryReader? _reader;
    private BinaryWriter? _writer;
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
        var theme = await ReadThemeAsync(cancellationToken).ConfigureAwait(false);
        var request = new ScreenshotWorkerRequest(
            precise,
            theme,
            CultureInfo.CurrentUICulture.Name,
            defaultAction,
            toolbarMode);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await EnsureWorkerAsync(cancellationToken).ConfigureAwait(false);
            var process = _process
                          ?? throw new InvalidOperationException("Screenshot worker was not initialized.");
            using var cancellationRegistration = cancellationToken.Register(
                static state => TryTerminate((Process)state!),
                process);
            try
            {
                ScreenshotWorkerProtocol.WriteRequest(_writer!, request);
                return await Task.Run(
                        () => ScreenshotWorkerProtocol.Read(_reader!),
                        CancellationToken.None)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ResetWorker();
                throw;
            }
            catch (Exception exception) when (exception is IOException
                                              or EndOfStreamException
                                              or InvalidDataException
                                              or ObjectDisposedException)
            {
                ResetWorker();
                throw;
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
        if (_process is { HasExited: false } && _pipe?.IsConnected == true)
            return;

        ResetWorker();
        var pipeName = "EasyChat.Screenshot." + Guid.NewGuid().ToString("N");
        NamedPipeServerStream? pipe = null;
        Process? process = null;
        BinaryReader? reader = null;
        BinaryWriter? writer = null;
        try
        {
            pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            process = StartWorker(pipeName);
            using var cancellationRegistration = cancellationToken.Register(
                static state => TryTerminate((Process)state!),
                process);
            using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            connectionCancellation.CancelAfter(ConnectionTimeout);
            try
            {
                await pipe.WaitForConnectionAsync(connectionCancellation.Token).ConfigureAwait(false);
                reader = new BinaryReader(pipe, System.Text.Encoding.UTF8, leaveOpen: true);
                writer = new BinaryWriter(pipe, System.Text.Encoding.UTF8, leaveOpen: true);
                await Task.Run(() => ScreenshotWorkerProtocol.ReadReady(reader), CancellationToken.None)
                    .WaitAsync(connectionCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Screenshot worker did not become ready in time.");
            }

            _pipe = pipe;
            _process = process;
            _reader = reader;
            _writer = writer;
            pipe = null;
            process = null;
            reader = null;
            writer = null;
        }
        finally
        {
            writer?.Dispose();
            reader?.Dispose();
            pipe?.Dispose();
            if (process is not null)
            {
                TryTerminate(process);
                TryWaitForExit(process, milliseconds: 5000);
                process.Dispose();
            }
        }
    }

    private static async Task<string> ReadThemeAsync(CancellationToken cancellationToken)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return ReadTheme();
        return await Dispatcher.UIThread.InvokeAsync(
            ReadTheme,
            DispatcherPriority.Normal,
            cancellationToken);
    }

    private static string ReadTheme() =>
        Avalonia.Application.Current?.ActualThemeVariant == ThemeVariant.Dark
            ? "Dark"
            : Avalonia.Application.Current?.ActualThemeVariant == ThemeVariant.Light
                ? "Light"
                : "Default";

    private static Process StartWorker(string pipeName)
    {
        var executable = Environment.ProcessPath
                         ?? throw new InvalidOperationException("Unable to locate the EasyChat executable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("--screenshot-worker");
        startInfo.ArgumentList.Add(pipeName);
        return Process.Start(startInfo)
               ?? throw new InvalidOperationException("Unable to start the screenshot worker process.");
    }

    private void ResetWorker()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _pipe?.Dispose();
        _writer = null;
        _reader = null;
        _pipe = null;

        if (_process is not { } process)
            return;
        _process = null;
        if (!TryWaitForExit(process, milliseconds: 250))
        {
            TryTerminate(process);
            TryWaitForExit(process, milliseconds: 5000);
        }
        process.Dispose();
    }

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
}
