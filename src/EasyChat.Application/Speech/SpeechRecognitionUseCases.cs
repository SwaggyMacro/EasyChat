using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EasyChat.Application.Translation;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Speech;
using EasyChat.Contracts.Translation;
using EasyChat.Shared.Results;
using Microsoft.Extensions.Logging;

namespace EasyChat.Application.Speech;

public sealed class SpeechRecognitionUseCases : ISpeechRecognitionUseCases
{
    private readonly ISpeechRecognitionEngine _engine;
    private readonly IPlatformAccessUseCases _platformAccess;
    private readonly ISettingsUseCases _settings;
    private readonly ITranslationUseCases _translation;
    private readonly ITranslationLanguageCatalog _languages;
    private readonly ITtsUseCases? _tts;
    private readonly ILogger<SpeechRecognitionUseCases> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SubtitleTranslationLane _subtitleAiTranslationLane = new();
    private readonly SubtitleTranslationLane _subtitleMachineTranslationLane = new();
    private readonly SubtitleFloatingLifecycleRegistry _subtitleFloatingLifecycle;
    private readonly SubtitleTimestampClock _subtitleTimestampClock;
    private long _nextSubtitleId;

    public SpeechRecognitionUseCases(
        ISpeechRecognitionEngine engine,
        IPlatformAccessUseCases platformAccess,
        ISettingsUseCases settings,
        ITranslationUseCases translation,
        ITranslationLanguageCatalog languages,
        ILogger<SpeechRecognitionUseCases> logger,
        ITtsUseCases? tts = null)
        : this(
            engine,
            platformAccess,
            settings,
            translation,
            languages,
            logger,
            TimeProvider.System,
            tts)
    {
    }

    internal SpeechRecognitionUseCases(
        ISpeechRecognitionEngine engine,
        IPlatformAccessUseCases platformAccess,
        ISettingsUseCases settings,
        ITranslationUseCases translation,
        ITranslationLanguageCatalog languages,
        ILogger<SpeechRecognitionUseCases> logger,
        TimeProvider timeProvider,
        ITtsUseCases? tts = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _platformAccess = platformAccess ?? throw new ArgumentNullException(nameof(platformAccess));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _translation = translation ?? throw new ArgumentNullException(nameof(translation));
        _languages = languages ?? throw new ArgumentNullException(nameof(languages));
        _tts = tts;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _subtitleFloatingLifecycle = new SubtitleFloatingLifecycleRegistry(_timeProvider);
        _subtitleTimestampClock = new SubtitleTimestampClock(_timeProvider);
    }

    public async IAsyncEnumerable<SpeechSessionEvent> RecognizeAsync(
        SpeechRecognitionCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var access = await _platformAccess.EnsureAvailableAsync(
            PlatformCapability.SpeechRecognition,
            cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
        {
            yield return new SpeechSessionErrorEvent(access.Error.Message);
            yield return new SpeechSessionStoppedEvent();
            yield break;
        }

        foreach (var permission in RequiredPermissions(command.Sources))
        {
            var permissionAccess = await _platformAccess.EnsurePermissionAsync(
                permission,
                cancellationToken).ConfigureAwait(false);
            if (permissionAccess.IsFailure)
            {
                yield return new SpeechSessionErrorEvent(permissionAccess.Error.Message);
                yield return new SpeechSessionStoppedEvent();
                yield break;
            }
        }

        using var engineLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var workflowLifetime = command.CompleteOnCancellation
            ? new CancellationTokenSource()
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var events = Channel.CreateUnbounded<SpeechSessionEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        var pump = PumpAsync(command, events.Writer, engineLifetime.Token, workflowLifetime.Token);
        try
        {
            var readerToken = command.CompleteOnCancellation
                ? workflowLifetime.Token
                : cancellationToken;
            await foreach (var item in events.Reader.ReadAllAsync(readerToken).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            engineLifetime.Cancel();
            workflowLifetime.Cancel();
            try
            {
                await pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (engineLifetime.IsCancellationRequested)
            {
            }
        }
    }

    public async ValueTask<Result> PrepareAsync(
        SpeechRecognitionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var access = await _platformAccess.EnsureAvailableAsync(
            PlatformCapability.SpeechRecognition,
            cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
            return Result.Failure(access.Error);

        foreach (var permission in RequiredPermissions(command.Sources))
        {
            var permissionAccess = await _platformAccess.EnsurePermissionAsync(
                permission,
                cancellationToken).ConfigureAwait(false);
            if (permissionAccess.IsFailure)
                return Result.Failure(permissionAccess.Error);
        }

        try
        {
            await _engine.PrepareAsync(ToOptions(command), cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Speech recognition preparation failed.");
            return Result.Failure(new Error("speech.preparation_failed", exception.Message));
        }
    }

    public async ValueTask<Result> ReleasePreparationAsync(
        SpeechRecognitionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        try
        {
            await _engine.ReleasePreparationAsync(ToOptions(command), cancellationToken)
                .ConfigureAwait(false);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Speech recognition preparation cleanup failed.");
            return Result.Failure(new Error("speech.preparation_cleanup_failed", exception.Message));
        }
    }

    private async Task PumpAsync(
        SpeechRecognitionCommand command,
        ChannelWriter<SpeechSessionEvent> writer,
        CancellationToken engineCancellationToken,
        CancellationToken workflowCancellationToken)
    {
        SpeechRecognitionSettings GetSettings()
        {
            var speech = command.SettingsOverride ?? _settings.Current.SpeechRecognition;
            var general = _settings.Current.General;
            var effectiveEngine = TranslationConfigurationResolver.ResolveProvider(
                speech.EngineId,
                general,
                TranslationEngineNames.AiModel);
            var useMachine = string.Equals(
                effectiveEngine,
                TranslationEngineNames.MachineTrans,
                StringComparison.OrdinalIgnoreCase);
            return speech with
            {
                EngineId = useMachine
                    ? TranslationConfigurationResolver.ResolveMachineProvider(
                        speech.EngineId,
                        general,
                        string.Empty)
                    : TranslationConfigurationResolver.ResolveAiModelId(speech.EngineId, general) ?? string.Empty,
                EngineType = TranslationConfigurationResolver.IsGlobal(speech.EngineId)
                    ? useMachine ? 0 : 1
                    : speech.EngineType,
                PromptId = TranslationConfigurationResolver.ResolvePromptId(
                    speech.PromptId,
                    _settings.Current.Prompts)
            };
        }
        void Publish(SpeechSessionEvent item) => writer.TryWrite(item);
        try
        {
            var coordinator = new SubtitleSessionCoordinator(
                GetSettings,
                _translation,
                _languages,
                _logger,
                _timeProvider,
                () => Interlocked.Increment(ref _nextSubtitleId),
                Publish,
                _subtitleAiTranslationLane,
                _subtitleMachineTranslationLane,
                _subtitleFloatingLifecycle,
                _subtitleTimestampClock,
                _tts,
                command.SubtitleOrigin,
                command.SegmentationMode == SpeechRecognitionSegmentationMode.SingleUtterance);
            var recognition = _engine.RecognizeAsync(
                ToOptions(command),
                engineCancellationToken);
            if (command.CompleteOnCancellation)
                recognition = CompleteRecognitionOnCancellationAsync(
                    recognition,
                    engineCancellationToken);

            await coordinator.RunAsync(
                recognition,
                workflowCancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (engineCancellationToken.IsCancellationRequested)
        {
            if (command.CompleteOnCancellation && !workflowCancellationToken.IsCancellationRequested)
                writer.TryWrite(new SpeechSessionStoppedEvent());
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Speech recognition workflow failed.");
            Publish(new SpeechSessionErrorEvent(exception.Message));
            Publish(new SpeechSessionStoppedEvent());
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private static async IAsyncEnumerable<SpeechRecognitionEvent> CompleteRecognitionOnCancellationAsync(
        IAsyncEnumerable<SpeechRecognitionEvent> recognition,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stopped = false;
        await using var enumerator = recognition.GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            bool hasNext;
            try
            {
                hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (!hasNext)
                break;
            var item = enumerator.Current;
            if (item.Kind == SpeechRecognitionEventKind.Stopped)
                stopped = true;
            yield return item;
        }
        if (!stopped)
            yield return new SpeechRecognitionEvent(SpeechRecognitionEventKind.Stopped);
    }

    private static IEnumerable<PlatformPermission> RequiredPermissions(
        IReadOnlyList<AudioCaptureSourceReference> sources)
    {
        if (sources.Count == 0)
            return [PlatformPermission.SystemAudioCapture];

        return sources.Select(source => source.Kind == AudioCaptureSourceKind.Microphone
                ? PlatformPermission.Microphone
                : PlatformPermission.SystemAudioCapture)
            .Distinct();
    }

    private static SpeechRecognitionOptions ToOptions(SpeechRecognitionCommand command) =>
        new(
            command.ModelPath,
            command.Language,
            command.Sources.Select(source => source.Token).ToArray(),
            command.AllowConcurrentSessions);
}
