using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Speech;
using EasyChat.Shared.Results;

namespace EasyChat.Application.Speech;

public sealed class TtsUseCases : ITtsUseCases
{
    private readonly IReadOnlyList<ITtsSynthesisProvider> _providers;
    private readonly ISettingsUseCases _settings;
    private readonly ITtsOutputWriter _outputWriter;
    private readonly IAudioPlaybackQueue _playbackQueue;
    private readonly IAudioPlaybackDeviceCatalog? _playbackDevices;
    private readonly object _sessionSync = new();
    private readonly SemaphoreSlim _enqueueGate = new(1, 1);
    private readonly Dictionary<Guid, TtsPlaybackSession> _sessions = [];
    private readonly HashSet<Guid> _recentlyCompletedPlaybackIds = [];

    public TtsUseCases(
        IEnumerable<ITtsSynthesisProvider> providers,
        ISettingsUseCases settings,
        ITtsOutputWriter outputWriter,
        IAudioPlaybackQueue playbackQueue,
        IAudioPlaybackDeviceCatalog? playbackDevices = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToArray();
        if (_providers.Count == 0)
            throw new InvalidOperationException("No TTS provider is registered.");
        if (_providers.Select(provider => provider.ProviderId).Distinct(StringComparer.Ordinal).Count()
            != _providers.Count)
        {
            throw new InvalidOperationException("TTS provider IDs must be unique.");
        }

        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _outputWriter = outputWriter ?? throw new ArgumentNullException(nameof(outputWriter));
        _playbackQueue = playbackQueue ?? throw new ArgumentNullException(nameof(playbackQueue));
        _playbackDevices = playbackDevices;
        if (playbackQueue is IAudioPlaybackLifecycle lifecycle)
        {
            lifecycle.PlaybackCompleted += OnPlaybackCompleted;
        }
    }

    public IReadOnlyList<TtsProviderDescriptor> GetProviders() =>
        _providers.Select(provider => new TtsProviderDescriptor(provider.ProviderId)).ToArray();

    public ValueTask<Result<IReadOnlyList<TtsVoice>>> GetVoicesAsync(
        string? providerId = null,
        CancellationToken cancellationToken = default) =>
        ResolveProvider(providerId).GetVoicesAsync(cancellationToken);

    public ValueTask<Result<IReadOnlyList<TtsLanguage>>> GetLanguagesAsync(
        string? providerId = null,
        CancellationToken cancellationToken = default) =>
        ResolveProvider(providerId).GetLanguagesAsync(cancellationToken);

    public async ValueTask<Result<string?>> ResolvePreferredVoiceAsync(
        string languageId,
        string? providerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageId);
        var provider = ResolveProvider(providerId);
        var preferences = _settings.Current.Tts.ProviderVoicePreferences;
        preferences.TryGetValue(provider.ProviderId, out var voices);
        if (voices is not null
            && TryResolveConfiguredVoice(
                voices,
                languageId,
                exactOnly: true,
                genericOnly: false,
                out var configuredVoice))
        {
            return Result<string?>.Success(configuredVoice);
        }

        var available = await provider.GetVoicesAsync(cancellationToken).ConfigureAwait(false);
        if (available.IsFailure)
            return Result<string?>.Failure(available.Error);

        var hasRegion = languageId.Contains('-', StringComparison.Ordinal);
        var voice = hasRegion
            ? available.Value.FirstOrDefault(candidate =>
                string.Equals(candidate.LanguageId, languageId, StringComparison.OrdinalIgnoreCase))
            : null;
        if (voice is not null)
            return Result<string?>.Success(voice.Id);

        if (voices is not null
            && TryResolveConfiguredVoice(
                voices,
                languageId,
                exactOnly: hasRegion,
                genericOnly: hasRegion,
                out configuredVoice))
        {
            return Result<string?>.Success(configuredVoice);
        }

        var language = GetPrimaryLanguage(languageId);
        voice ??= available.Value.FirstOrDefault(candidate =>
            string.Equals(GetPrimaryLanguage(candidate.LanguageId), language, StringComparison.OrdinalIgnoreCase));
        voice ??= available.Value.FirstOrDefault(candidate =>
            candidate.LanguageId.StartsWith(language, StringComparison.OrdinalIgnoreCase));
        voice ??= available.Value.FirstOrDefault(candidate =>
            candidate.LanguageId.StartsWith("en-", StringComparison.OrdinalIgnoreCase)
            || candidate.Id.Contains("en", StringComparison.OrdinalIgnoreCase));
        return Result<string?>.Success(voice?.Id);
    }

    private static bool TryResolveConfiguredVoice(
        IReadOnlyDictionary<string, string> voices,
        string languageId,
        bool exactOnly,
        bool genericOnly,
        out string configuredVoice)
    {
        var exact = voices.FirstOrDefault(pair =>
            string.Equals(pair.Key, languageId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(exact.Value))
        {
            configuredVoice = exact.Value;
            return true;
        }
        if (exactOnly)
        {
            configuredVoice = string.Empty;
            return false;
        }

        var primaryLanguage = GetPrimaryLanguage(languageId);
        var configured = voices
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value)
                           && (!genericOnly || !pair.Key.Contains('-', StringComparison.Ordinal))
                           && string.Equals(
                               GetPrimaryLanguage(pair.Key),
                               primaryLanguage,
                               StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(configured.Value))
        {
            configuredVoice = configured.Value;
            return true;
        }

        configuredVoice = string.Empty;
        return false;
    }

    private static string GetPrimaryLanguage(string languageId) =>
        languageId.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];

    public ValueTask<Result<AudioTrack>> SynthesizeAsync(
        TtsSynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        return ResolveProvider(request.ProviderId).SynthesizeAsync(request, cancellationToken);
    }

    public async ValueTask<Result> SynthesizeToFileAsync(
        TtsSynthesisRequest request,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var synthesized = await SynthesizeAsync(request, cancellationToken).ConfigureAwait(false);
        return synthesized.IsFailure
            ? Result.Failure(synthesized.Error)
            : await _outputWriter.WriteAsync(outputPath, synthesized.Value, cancellationToken)
                .ConfigureAwait(false);
    }

    public ValueTask<Result> EnqueueAsync(
        TtsSynthesisRequest request,
        bool interruptCurrent = false,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(request, interruptCurrent, AudioPlaybackTarget.Default,
            AudioPlaybackContext.None, cancellationToken);

    public async ValueTask<Result> EnqueueAsync(
        TtsSynthesisRequest request,
        bool interruptCurrent,
        AudioPlaybackTarget target,
        CancellationToken cancellationToken = default)
        => await EnqueueAsync(request, interruptCurrent, target, AudioPlaybackContext.None, cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask<Result> EnqueueAsync(
        TtsSynthesisRequest request,
        bool interruptCurrent,
        AudioPlaybackTarget target,
        AudioPlaybackContext context,
        CancellationToken cancellationToken = default) =>
        await EnqueueAsync(request, interruptCurrent, target, context, null, cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask<Result> EnqueueAsync(
        TtsSynthesisRequest request,
        bool interruptCurrent,
        AudioPlaybackTarget target,
        AudioPlaybackContext context,
        string? outputDeviceName,
        CancellationToken cancellationToken = default)
    {
        var result = await EnqueueWithSessionAsync(
            request, interruptCurrent, target, context, outputDeviceName, cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    public ValueTask<Result<ITtsPlaybackSession>> EnqueueWithSessionAsync(
        TtsSynthesisRequest request,
        bool interruptCurrent = false,
        CancellationToken cancellationToken = default) =>
        EnqueueWithSessionAsync(request, interruptCurrent, AudioPlaybackTarget.Default,
            AudioPlaybackContext.None, cancellationToken);

    public ValueTask<Result<ITtsPlaybackSession>> EnqueueWithSessionAsync(
        TtsSynthesisRequest request,
        bool interruptCurrent,
        AudioPlaybackTarget target,
        CancellationToken cancellationToken = default) =>
        EnqueueWithSessionAsync(request, interruptCurrent, target, AudioPlaybackContext.None, null, cancellationToken);

    public async ValueTask<Result<ITtsPlaybackSession>> EnqueueWithSessionAsync(
        TtsSynthesisRequest request,
        bool interruptCurrent,
        AudioPlaybackTarget target,
        AudioPlaybackContext context,
        CancellationToken cancellationToken = default) =>
        await EnqueueWithSessionAsync(
            request, interruptCurrent, target, context, null, cancellationToken).ConfigureAwait(false);

    private async ValueTask<Result<ITtsPlaybackSession>> EnqueueWithSessionAsync(
        TtsSynthesisRequest request,
        bool interruptCurrent,
        AudioPlaybackTarget target,
        AudioPlaybackContext context,
        string? outputDeviceName,
        CancellationToken cancellationToken)
    {
        var synthesized = await SynthesizeAsync(request, cancellationToken).ConfigureAwait(false);
        if (synthesized.IsFailure)
            return Result<ITtsPlaybackSession>.Failure(synthesized.Error);

        AudioPlaybackDeviceToken? deviceToken = null;
        if (target is AudioPlaybackTarget.VirtualCable or AudioPlaybackTarget.SelectedDevice)
        {
            if (_playbackDevices is null)
            {
                return Result<ITtsPlaybackSession>.Failure(new Error(
                    "audio.virtual_cable_unavailable",
                    "VB-Audio Cable playback device 'CABLE Input' is not available."));
            }

            IReadOnlyList<AudioPlaybackDeviceDescriptor> devices;
            try
            {
                devices = await _playbackDevices.GetDevicesAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Result<ITtsPlaybackSession>.Failure(new Error(
                    "audio.virtual_cable_unavailable",
                    exception.Message));
            }
            AudioPlaybackDeviceDescriptor? selected = target == AudioPlaybackTarget.SelectedDevice
                ? devices.FirstOrDefault(device =>
                    !string.IsNullOrWhiteSpace(outputDeviceName)
                    && string.Equals(device.Name, outputDeviceName, StringComparison.OrdinalIgnoreCase))
                : devices.FirstOrDefault(device => device.IsVirtualCable);
            if (selected is null)
            {
                return Result<ITtsPlaybackSession>.Failure(new Error(
                    target == AudioPlaybackTarget.SelectedDevice
                        ? "audio.playback_device_unavailable"
                        : "audio.virtual_cable_unavailable",
                    target == AudioPlaybackTarget.SelectedDevice
                        ? $"Audio playback device '{outputDeviceName}' is not available."
                        : "VB-Audio Cable playback device 'CABLE Input' is not available."));
            }
            deviceToken = selected.Token;
        }

        await _enqueueGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (interruptCurrent)
                await StopAllSessionsAsync(cancellationToken).ConfigureAwait(false);

            Guid playbackId;
            if (target == AudioPlaybackTarget.Default && context == AudioPlaybackContext.None)
            {
                playbackId = await _playbackQueue.EnqueueAsync(
                    synthesized.Value,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                playbackId = await _playbackQueue.EnqueueAsync(
                    synthesized.Value,
                    target,
                    context,
                    deviceToken,
                    cancellationToken).ConfigureAwait(false);
            }

            var session = new TtsPlaybackSession(this, playbackId);
            lock (_sessionSync)
            {
                if (_recentlyCompletedPlaybackIds.Remove(playbackId))
                    session.Complete();
                else
                    _sessions[playbackId] = session;
            }
            return Result<ITtsPlaybackSession>.Success(session);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result<ITtsPlaybackSession>.Failure(new Error("audio.playback_unavailable", exception.Message));
        }
        finally
        {
            _enqueueGate.Release();
        }
    }

    private async ValueTask StopAllSessionsAsync(CancellationToken cancellationToken)
    {
        TtsPlaybackSession[] sessions;
        lock (_sessionSync)
        {
            sessions = _sessions.Values.ToArray();
            _sessions.Clear();
            foreach (var session in sessions)
                session.Complete();
        }
        await _playbackQueue.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask StopSessionAsync(TtsPlaybackSession session, CancellationToken cancellationToken)
    {
        var shouldStop = false;
        lock (_sessionSync)
        {
            if (_sessions.ContainsKey(session.PlaybackId))
            {
                shouldStop = true;
                var sessions = _sessions.Values.ToArray();
                _sessions.Clear();
                foreach (var current in sessions)
                    current.Complete();
            }
        }
        if (shouldStop)
            await _playbackQueue.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OnPlaybackCompleted(object? sender, AudioPlaybackEventArgs args)
    {
        TtsPlaybackSession? session = null;
        lock (_sessionSync)
        {
            if (_sessions.Remove(args.PlaybackId, out session))
                session.Complete();
            else
            {
                _recentlyCompletedPlaybackIds.Add(args.PlaybackId);
                if (_recentlyCompletedPlaybackIds.Count > 64)
                    _recentlyCompletedPlaybackIds.Remove(_recentlyCompletedPlaybackIds.First());
            }
        }
    }

    private ITtsSynthesisProvider ResolveProvider(string? requestedId)
    {
        var providerId = string.IsNullOrWhiteSpace(requestedId)
            ? _settings.Current.Tts.Provider
            : requestedId;
        return _providers.FirstOrDefault(provider => provider.ProviderId == providerId)
               ?? _providers.FirstOrDefault(provider => provider.ProviderId == TtsProviderIds.EdgeTts)
               ?? _providers[0];
    }

    private static void Validate(TtsSynthesisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Text);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.VoiceId);
    }

    private sealed class TtsPlaybackSession(TtsUseCases owner, Guid playbackId) : ITtsPlaybackSession
    {
        private readonly TaskCompletionSource<object?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Guid PlaybackId { get; } = playbackId;
        public Task Completion => _completion.Task;

        public ValueTask StopAsync(CancellationToken cancellationToken = default) =>
            owner.StopSessionAsync(this, cancellationToken);

        public void Complete() => _completion.TrySetResult(null);
    }
}
