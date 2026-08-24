using EasyChat.Contracts.Input;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Translation;
using EasyChat.Shared.Results;
using Microsoft.Extensions.Logging;

namespace EasyChat.Application.Input;

public sealed class TsfInputTranslationCoordinator(
    ISettingsUseCases settings,
    ITranslationLanguageCatalog languages,
    ITranslationUseCases translation,
    ITextServicesFrameworkBridge bridge,
    ILogger<TsfInputTranslationCoordinator> logger) : ITsfInputTranslationUseCases
{
    private static readonly TimeSpan PreviewDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan FinalDelay = TimeSpan.FromMilliseconds(500);
    private readonly object _gate = new();
    private readonly Dictionary<TsfSessionToken, SessionState> _sessions = [];
    private readonly ISettingsUseCases _settings = settings;
    private readonly ITranslationLanguageCatalog _languages = languages;
    private readonly ITranslationUseCases _translation = translation;
    private readonly ITextServicesFrameworkBridge _bridge = bridge;
    private readonly ILogger<TsfInputTranslationCoordinator> _logger = logger;
    private int _started;

    public event EventHandler<TsfCandidateChanged>? CandidateChanged;

    public TextServicesFrameworkStatus Status => _bridge.Status;

    public ValueTask<Result> StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _bridge.CompositionChanged += OnCompositionChanged;
            _bridge.CompositionEnded += OnCompositionEnded;
        }

        return _bridge.StartAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _bridge.CompositionChanged -= OnCompositionChanged;
        _bridge.CompositionEnded -= OnCompositionEnded;
        SessionState[] states;
        lock (_gate)
        {
            states = _sessions.Values.ToArray();
            _sessions.Clear();
        }

        foreach (var state in states)
            state.Dispose();
        await _bridge.DisposeAsync().ConfigureAwait(false);
    }

    private void OnCompositionChanged(object? sender, TsfCompositionChanged change)
    {
        if (_settings.Current.Input.TranslationMode != InputTranslationMode.Tsf)
            return;

        if (change.IsPasswordField)
        {
            Publish(new TsfCandidateChanged(
                change.Session,
                change.Revision,
                change.Text,
                string.Empty,
                TsfCandidateStatus.Unsupported,
                change.CaretRegion,
                "Translation is disabled for protected input."));
            _ = _bridge.CancelAsync(change.Session, change.Revision);
            return;
        }

        SessionState state;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(change.Session, out state!))
            {
                state = new SessionState();
                _sessions.Add(change.Session, state);
            }
            state.Replace(change);
        }

        Publish(new TsfCandidateChanged(
            change.Session,
            change.Revision,
            change.Text,
            string.Empty,
            TsfCandidateStatus.Translating,
            change.CaretRegion));

        _ = TranslateAsync(change, state, final: false, delay: PreviewDelay);
        // Every edit gets a quiet-window commit. Punctuation short-circuits the wait so a
        // sentence boundary is committed as soon as the provider result is available.
        _ = TranslateAfterPauseAsync(change, state);
    }

    private void OnCompositionEnded(object? sender, TsfCompositionEnded ended)
    {
        SessionState? state;
        lock (_gate)
        {
            _sessions.Remove(ended.Session, out state);
        }
        state?.Dispose();
        Publish(new TsfCandidateChanged(
            ended.Session,
            ended.Revision,
            string.Empty,
            string.Empty,
            TsfCandidateStatus.Hidden,
            null));
    }

    private async Task TranslateAfterPauseAsync(TsfCompositionChanged change, SessionState state)
    {
        try
        {
            if (!change.IsSentenceBoundary)
                await Task.Delay(FinalDelay, state.Token).ConfigureAwait(false);
            if (!state.TryBeginFinal(change.Revision))
                return;
            await TranslateAsync(change, state, final: true, delay: TimeSpan.Zero).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (state.Token.IsCancellationRequested)
        {
        }
    }

    private async Task TranslateAsync(
        TsfCompositionChanged change,
        SessionState state,
        bool final,
        TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, state.Token).ConfigureAwait(false);
            if (!IsCurrent(change, state, final) || string.IsNullOrWhiteSpace(change.Text))
                return;

            var input = _settings.Current.Input;
            var general = _settings.Current.General;
            var sourceId = input.FollowGlobalLanguage ? general.SourceLanguage.Id : input.TypingSourceLanguage;
            var targetId = input.FollowGlobalLanguage ? general.TargetLanguage.Id : input.TypingTargetLanguage;
            if (input.ReverseTranslateLanguage)
                (sourceId, targetId) = (targetId, sourceId);

            var translated = await _translation.TranslateAsync(
                new TranslationRequest(
                    change.Text,
                    _languages.Get(sourceId),
                    _languages.Get(targetId),
                    PlainText: true),
                state.Token).ConfigureAwait(false);
            if (translated.IsFailure)
            {
                Publish(new TsfCandidateChanged(
                    change.Session,
                    change.Revision,
                    change.Text,
                    string.Empty,
                    TsfCandidateStatus.Failed,
                    change.CaretRegion,
                    translated.Error.Message));
                return;
            }

            var text = translated.Value.Text;
            if (string.IsNullOrWhiteSpace(text) || !IsCurrent(change, state, final))
                return;

            var update = new TsfTranslationUpdate(change.Session, change.Revision, text, final);
            var delivered = final
                ? await _bridge.CommitAsync(update, state.Token).ConfigureAwait(false)
                : await _bridge.SendPreviewAsync(update, state.Token).ConfigureAwait(false);
            if (delivered.IsFailure)
            {
                Publish(new TsfCandidateChanged(
                    change.Session,
                    change.Revision,
                    change.Text,
                    string.Empty,
                    TsfCandidateStatus.Failed,
                    change.CaretRegion,
                    delivered.Error.Message));
                return;
            }

            Publish(new TsfCandidateChanged(
                change.Session,
                change.Revision,
                change.Text,
                text,
                final ? TsfCandidateStatus.Committed : TsfCandidateStatus.Preview,
                change.CaretRegion));
        }
        catch (OperationCanceledException) when (state.Token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "TSF input translation failed for {Session}.", change.Session.Value);
            Publish(new TsfCandidateChanged(
                change.Session,
                change.Revision,
                change.Text,
                string.Empty,
                TsfCandidateStatus.Failed,
                change.CaretRegion,
                exception.Message));
        }
    }

    private bool IsCurrent(TsfCompositionChanged change, SessionState state, bool final = false) =>
        !state.Token.IsCancellationRequested
        && state.Revision == change.Revision
        && (final || !state.FinalStarted)
        && _settings.Current.Input.TranslationMode == InputTranslationMode.Tsf;

    private void Publish(TsfCandidateChanged changed) => CandidateChanged?.Invoke(this, changed);

    private sealed class SessionState : IDisposable
    {
        private CancellationTokenSource _cancellation = new();
        private int _finalStarted;

        public CancellationToken Token => _cancellation.Token;
        public long Revision { get; private set; }
        public bool FinalStarted => Volatile.Read(ref _finalStarted) != 0;

        public void Replace(TsfCompositionChanged change)
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
            _cancellation = new CancellationTokenSource();
            Revision = change.Revision;
            Volatile.Write(ref _finalStarted, 0);
        }

        public bool TryBeginFinal(long revision)
        {
            if (_cancellation.IsCancellationRequested || Revision != revision
                || Interlocked.CompareExchange(ref _finalStarted, 1, 0) != 0)
                return false;
            return true;
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
        }
    }
}

internal sealed class UnsupportedTextServicesFrameworkBridge : ITextServicesFrameworkBridge
{
    public event EventHandler<TsfCompositionChanged>? CompositionChanged;
    public event EventHandler<TsfCompositionEnded>? CompositionEnded;

    public TextServicesFrameworkStatus Status { get; private set; } =
        new(TextServicesFrameworkState.Unsupported, "TSF is not available on this platform.");

    public ValueTask<Result> StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Result.Failure(new Error("tsf.unsupported", Status.Message!)));
    }

    public ValueTask<Result> SendPreviewAsync(TsfTranslationUpdate update, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Result.Failure(new Error("tsf.unsupported", Status.Message!)));

    public ValueTask<Result> CommitAsync(TsfTranslationUpdate update, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Result.Failure(new Error("tsf.unsupported", Status.Message!)));

    public ValueTask<Result> CancelAsync(TsfSessionToken session, long revision, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Result.Failure(new Error("tsf.unsupported", Status.Message!)));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
