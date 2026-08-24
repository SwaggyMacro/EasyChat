using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using EasyChat.Contracts.Platform;
using EasyChat.Shared.Results;
using Microsoft.Extensions.Logging;

namespace EasyChat.Infrastructure.Windows.Input;

public sealed class WindowsTsfInputBridge(
    IWindowsTsfRegistration registration,
    ILogger<WindowsTsfInputBridge> logger) : ITextServicesFrameworkBridge
{
    private const int ProtocolVersion = 1;
    private static readonly JsonSerializerOptions PipeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    private readonly IWindowsTsfRegistration _registration = registration;
    private readonly ILogger<WindowsTsfInputBridge> _logger = logger;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _gate = new();
    private CancellationTokenSource? _lifetime;
    private Task? _serverTask;
    private NamedPipeServerStream? _pipe;

    public event EventHandler<TsfCompositionChanged>? CompositionChanged;
    public event EventHandler<TsfCompositionEnded>? CompositionEnded;

    public TextServicesFrameworkStatus Status { get; private set; } =
        new(TextServicesFrameworkState.NotActive, "TSF has not been started.");

    public async ValueTask<Result> StartAsync(CancellationToken cancellationToken = default)
    {
        var registered = await _registration.EnsureRegisteredAsync(cancellationToken).ConfigureAwait(false);
        Status = _registration.Status;
        if (registered.IsFailure)
            return registered;

        lock (_gate)
        {
            if (_serverTask is not null)
                return Result.Success();
            _lifetime = new CancellationTokenSource();
            _serverTask = Task.Run(() => RunServerAsync(_lifetime.Token), CancellationToken.None);
        }
        return Result.Success();
    }

    public async ValueTask<Result> SendPreviewAsync(TsfTranslationUpdate update, CancellationToken cancellationToken = default) =>
        await SendAsync(new PipeMessage("translation.preview", update.Session.Value, update.Revision, update.Text, false), cancellationToken).ConfigureAwait(false);

    public async ValueTask<Result> CommitAsync(TsfTranslationUpdate update, CancellationToken cancellationToken = default) =>
        await SendAsync(new PipeMessage("translation.commit", update.Session.Value, update.Revision, update.Text, true), cancellationToken).ConfigureAwait(false);

    public async ValueTask<Result> CancelAsync(TsfSessionToken session, long revision, CancellationToken cancellationToken = default) =>
        await SendAsync(new PipeMessage("translation.cancel", session.Value, revision, string.Empty, false), cancellationToken).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? lifetime;
        Task? serverTask;
        NamedPipeServerStream? pipe;
        lock (_gate)
        {
            lifetime = _lifetime;
            serverTask = _serverTask;
            pipe = _pipe;
            _lifetime = null;
            _serverTask = null;
            _pipe = null;
        }

        lifetime?.Cancel();
        pipe?.Dispose();
        if (serverTask is not null)
        {
            try { await serverTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        lifetime?.Dispose();
        _writeGate.Dispose();
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        var pipeName = GetPipeName();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = NamedPipeServerStreamAcl.Create(
                    pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    64 * 1024,
                    64 * 1024,
                    CreatePipeSecurity(),
                    HandleInheritability.None,
                    (PipeAccessRights)0);
                lock (_gate)
                    _pipe = pipe;
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                Status = new(TextServicesFrameworkState.Available, "TSF is connected.");
                await WriteAsync(pipe, new PipeMessage("hello", string.Empty, 0, string.Empty, false), cancellationToken).ConfigureAwait(false);
                await ReadClientAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Status = new(TextServicesFrameworkState.PipeUnavailable, exception.Message);
                _logger.LogWarning(exception, "TSF pipe client disconnected.");
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate)
                    _pipe = null;
                if (Status.State == TextServicesFrameworkState.Available)
                    Status = _registration.Status;
            }
        }
    }

    private async Task ReadClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var handshaken = false;
        while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                return;
            PipeMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<PipeMessage>(line, PipeJsonOptions);
            }
            catch (JsonException exception)
            {
                Status = new(TextServicesFrameworkState.PipeUnavailable, "The TSF pipe sent malformed JSON.");
                _logger.LogWarning(exception, "Ignoring malformed TSF pipe message.");
                await WriteAsync(
                    pipe,
                    new PipeMessage("error", string.Empty, 0, "malformed-json", false),
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            if (message is null || message.ProtocolVersion != ProtocolVersion)
            {
                Status = new(TextServicesFrameworkState.PipeUnavailable, "The TSF pipe protocol version is unsupported.");
                await WriteAsync(
                    pipe,
                    new PipeMessage("error", string.Empty, 0, "protocol-version-mismatch", false),
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            if (string.Equals(message.Type, "hello", StringComparison.Ordinal))
            {
                handshaken = true;
                continue;
            }

            if (!handshaken)
                continue;

            switch (message.Type)
            {
                case "composition.updated":
                    CompositionChanged?.Invoke(this, new TsfCompositionChanged(
                        new TsfSessionToken(message.Session),
                        message.Revision,
                        message.Text,
                        message.CaretWidth > 0 && message.CaretHeight > 0
                            ? new PhysicalScreenRegion(message.CaretX, message.CaretY, message.CaretWidth, message.CaretHeight)
                            : null,
                        message.IsFinal,
                        message.IsPassword));
                    break;
                case "composition.cancelled":
                    CompositionEnded?.Invoke(this, new TsfCompositionEnded(
                        new TsfSessionToken(message.Session), message.Revision, false));
                    break;
                case "composition.accepted":
                    CompositionEnded?.Invoke(this, new TsfCompositionEnded(
                        new TsfSessionToken(message.Session), message.Revision, true));
                    break;
            }
        }
    }

    private async ValueTask<Result> SendAsync(PipeMessage message, CancellationToken cancellationToken)
    {
        NamedPipeServerStream? pipe;
        lock (_gate)
            pipe = _pipe;
        if (pipe is null || !pipe.IsConnected)
            return Result.Failure(new Error("tsf.pipe-unavailable", "The TSF text service is not connected."));

        try
        {
            await WriteAsync(pipe, message, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception exception)
        {
            Status = new(TextServicesFrameworkState.PipeUnavailable, exception.Message);
            return Result.Failure(new Error("tsf.pipe-write-failed", exception.Message));
        }
    }

    private async ValueTask WriteAsync(NamedPipeServerStream pipe, PipeMessage message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, PipeJsonOptions) + "\n";
        var bytes = Encoding.UTF8.GetBytes(json);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await pipe.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static string GetPipeName()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? "unknown";
        return $"EasyChat.Tsf.{sid}";
    }

    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        var rights = PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize;
        var identities = new IdentityReference[]
        {
            WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("The current Windows SID is unavailable."),
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
        };
        foreach (var identity in identities)
            security.AddAccessRule(new PipeAccessRule(identity, rights, AccessControlType.Allow));
        return security;
    }

    private sealed record PipeMessage(
        string Type,
        string Session,
        long Revision,
        string Text,
        bool IsFinal,
        int ProtocolVersion = ProtocolVersion,
        int CaretX = 0,
        int CaretY = 0,
        int CaretWidth = 0,
        int CaretHeight = 0,
        bool IsPassword = false);
}
