using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using EasyChat.Application.Translation;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Speech;
using EasyChat.Contracts.Translation;
using Microsoft.Extensions.Logging;

namespace EasyChat.Application.Speech;

internal sealed class SubtitleSessionCoordinator
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);
    internal static readonly TimeSpan AiPreviewDebounce = TimeSpan.FromMilliseconds(650);
    internal static readonly TimeSpan AiPreviewMaximumWait = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan MachinePreviewDebounce = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan MachinePreviewMaximumWait = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan DisplayUpdateInterval = TimeSpan.FromMilliseconds(100);
    internal static readonly TimeSpan AiQuietPeriod = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan FinalTranslationRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProviderCancellationGracePeriod =
        TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan AiTranslationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MachineTranslationTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DuplicateFinalWindow = TimeSpan.FromMilliseconds(500);
    private const int MaximumTransientTranslationAttempts = 4;
    private const int MaximumTimedOutTranslationAttempts = 2;
    private const int MaximumInvalidStructuredPlanAttempts = 2;
    private const int MaximumStructuredSegments = 32;
    private const int AiMaximumWordsPerTranslation = IncrementalSubtitleSegmenter.MaximumWords * 2;
    private const int AiMaximumDisplayColumnsPerTranslation =
        IncrementalSubtitleSegmenter.MaximumDisplayColumns * 2;
    private const string SubtitlePrompt =
        "Translate live subtitles from [SourceLang] to [TargetLang]. "
        + "The user content is JSON with context and current fields. "
        + "Use context only to resolve meaning and translate only current. "
        + "Preserve an incomplete ending instead of inventing missing speech. "
        + "Follow the runtime JSONL output contract exactly.";
    private const string StructuredSubtitleContract = """
        The user content is a JSON object with `context` and `current` fields.
        Translate only `current`. Split it into consecutive semantic subtitle sentences.
        Every record must contain exactly one sentence. Never combine two terminal sentences
        in one record: for example, `A. B.` must produce two records, not one.
        Emit one raw JSON object per line with exactly this schema:
        {"seq":0,"source":"exact consecutive source slice","translation":"...","final":true}
        `seq` must start at 0 and increase by 1. `source` values concatenated in order must
        equal `current` exactly, including whitespace and punctuation. Do not omit, repeat,
        normalize, or paraphrase source text. `translation` must contain only the translation
        of that record's source. Set `final` to true for a complete sentence. Only the last
        record may use false when `current` ends with an incomplete sentence. Emit at most 32
        records. Do not emit Markdown, comments, events, arrays, or any other fields or text.
        """;

    private readonly Func<SpeechRecognitionSettings> _getSettings;
    private readonly ITranslationUseCases _translation;
    private readonly ITranslationLanguageCatalog _languages;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SubtitleTranslationLane _aiTranslationLane;
    private readonly SubtitleTranslationLane _machineTranslationLane;
    private readonly SubtitleFloatingLifecycleRegistry _floatingLifecycle;
    private readonly SubtitleTimestampClock _timestampClock;
    private readonly Func<long> _nextSubtitleId;
    private readonly Action<SpeechSessionEvent> _publish;
    private readonly IncrementalSubtitleSegmenter _segmenter = new();
    private readonly List<ManagedSubtitleLine> _floating = [];
    private readonly List<ManagedSubtitleLine> _sealedLines = [];
    private readonly List<UtteranceLineRange> _utteranceLines = [];
    private readonly LinkedList<TranslationJob> _finalJobs = [];
    private readonly Dictionary<long, ManagedSubtitleLine> _linesById = [];
    private readonly HashSet<TranslationProviderSelection> _unavailableTranslationSelections = [];
    private readonly HashSet<long> _announcedFloatingRemovals = [];
    private readonly Channel<SessionMessage> _inbox = Channel.CreateBounded<SessionMessage>(
        new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });

    private ManagedSubtitleLine? _currentLine;
    private UtteranceLineRange? _currentRange;
    private ManagedSubtitleLine? _lastUtteranceLine;
    private TranslationJob? _pendingPreview;
    private TranslationJob? _activeTranslation;
    private string _utteranceHypothesis = string.Empty;
    private string _lastFinalText = string.Empty;
    private TimeSpan? _lastFinalAt;
    private int _sentencesInCurrent;
    private long _nextTranslationJobId;
    private bool _recognitionStopped;
    private bool _stoppedPublished;
    private bool _hasPartialSinceFinal;
    private bool _started;

    public SubtitleSessionCoordinator(
        Func<SpeechRecognitionSettings> getSettings,
        ITranslationUseCases translation,
        ITranslationLanguageCatalog languages,
        ILogger logger,
        TimeProvider timeProvider,
        Func<long> nextSubtitleId,
        Action<SpeechSessionEvent> publish,
        SubtitleTranslationLane? aiTranslationLane = null,
        SubtitleTranslationLane? machineTranslationLane = null,
        SubtitleFloatingLifecycleRegistry? floatingLifecycle = null,
        SubtitleTimestampClock? timestampClock = null)
    {
        _getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
        _translation = translation ?? throw new ArgumentNullException(nameof(translation));
        _languages = languages ?? throw new ArgumentNullException(nameof(languages));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _aiTranslationLane = aiTranslationLane ?? new SubtitleTranslationLane();
        _machineTranslationLane = machineTranslationLane ?? new SubtitleTranslationLane();
        _floatingLifecycle = floatingLifecycle ?? new SubtitleFloatingLifecycleRegistry(_timeProvider);
        _timestampClock = timestampClock ?? new SubtitleTimestampClock(_timeProvider);
        _nextSubtitleId = nextSubtitleId ?? throw new ArgumentNullException(nameof(nextSubtitleId));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
    }

    public async Task RunAsync(
        IAsyncEnumerable<SpeechRecognitionEvent> recognition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recognition);
        ReplayFloatingRemovalTombstones();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var recognitionPump = PumpRecognitionAsync(recognition, lifetime.Token);
        var tickPump = PumpTicksAsync(lifetime.Token);
        try
        {
            while (await _inbox.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_inbox.Reader.TryRead(out var message))
                    await HandleAsync(message, lifetime.Token).ConfigureAwait(false);

                TryStartTranslation(lifetime.Token);
                if (_recognitionStopped && !HasTranslationWork)
                {
                    if (!_stoppedPublished)
                    {
                        _stoppedPublished = true;
                        _publish(new SpeechSessionStoppedEvent());
                    }
                    if (!HasPendingFloatingExpiry)
                        return;
                }
            }
        }
        finally
        {
            lifetime.Cancel();
            CancelAndDetachActiveTranslation();
            CancelPendingJobs();
            await IgnoreCancellationAsync(recognitionPump, lifetime.Token).ConfigureAwait(false);
            await IgnoreCancellationAsync(tickPump, lifetime.Token).ConfigureAwait(false);
        }
    }

    private bool HasTranslationWork =>
        _activeTranslation is not null || _pendingPreview is not null || _finalJobs.Count > 0;

    private bool HasPendingFloatingExpiry =>
        _floatingLifecycle.HasPendingExpiry();

    private async Task PumpRecognitionAsync(
        IAsyncEnumerable<SpeechRecognitionEvent> recognition,
        CancellationToken cancellationToken)
    {
        var stopped = false;
        try
        {
            await foreach (var item in recognition.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                await _inbox.Writer.WriteAsync(new RecognitionMessage(item), cancellationToken)
                    .ConfigureAwait(false);
                if (item.Kind == SpeechRecognitionEventKind.Stopped)
                {
                    stopped = true;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            await _inbox.Writer.WriteAsync(new RecognitionFailureMessage(exception), cancellationToken)
                .ConfigureAwait(false);
        }

        if (!stopped && !cancellationToken.IsCancellationRequested)
        {
            await _inbox.Writer.WriteAsync(
                    new RecognitionMessage(new SpeechRecognitionEvent(SpeechRecognitionEventKind.Stopped)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task PumpTicksAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TickInterval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await _inbox.Writer.WriteAsync(
                        new TickMessage(GetMonotonicNow()),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task HandleAsync(SessionMessage message, CancellationToken cancellationToken)
    {
        var now = GetMonotonicNow();
        if (!_getSettings().IsTranslationEnabled)
            CancelDisabledTranslationWork(now);

        switch (message)
        {
            case RecognitionMessage recognition:
                HandleRecognition(recognition.Event, now);
                break;
            case RecognitionFailureMessage failure:
                _logger.LogError(failure.Exception, "Speech recognition event pump failed.");
                _publish(new SpeechSessionErrorEvent(failure.Exception.Message));
                HandleRecognition(
                    new SpeechRecognitionEvent(SpeechRecognitionEventKind.Stopped),
                    now);
                break;
            case TickMessage tick:
                HandleTick(tick.Now);
                break;
            case TranslationBufferMessage buffer:
                HandleTranslationBuffer(buffer, now);
                break;
            case StructuredTranslationStartedMessage structuredStarted:
                HandleStructuredTranslationStarted(structuredStarted);
                break;
            case StructuredTranslationSegmentMessage segment:
                HandleStructuredTranslationSegment(segment, now);
                break;
            case TranslationCompletedMessage completed:
                HandleTranslationCompleted(completed, now);
                break;
        }
        await Task.CompletedTask;
    }

    private void HandleRecognition(SpeechRecognitionEvent item, TimeSpan now)
    {
        switch (item.Kind)
        {
            case SpeechRecognitionEventKind.Started:
                if (!_started)
                {
                    _started = true;
                    _publish(new SpeechSessionStartedEvent());
                }
                break;
            case SpeechRecognitionEventKind.Partial:
                _hasPartialSinceFinal = true;
                ApplySegmentation(_segmenter.ApplyPartial(item.Text ?? string.Empty, now), now);
                break;
            case SpeechRecognitionEventKind.Final:
                HandleFinal(item.Text ?? string.Empty, now);
                break;
            case SpeechRecognitionEventKind.Error:
                _publish(new SpeechSessionErrorEvent(item.Text ?? string.Empty));
                break;
            case SpeechRecognitionEventKind.Stopped:
                if (_recognitionStopped)
                    return;
                ApplySegmentation(_segmenter.CompleteLatest(), now);
                SealCurrentLine(now);
                _recognitionStopped = true;
                CancelPendingPreview();
                if (_activeTranslation is { IsFinal: false })
                    CancelAndDetachActiveTranslation();
                break;
        }
    }

    private void HandleTick(TimeSpan now)
    {
        if (!_recognitionStopped)
        {
            var quietPeriod = UsesBufferedAiTranslation(_getSettings())
                ? AiQuietPeriod
                : IncrementalSubtitleSegmenter.QuietPeriod;
            ApplySegmentation(_segmenter.Tick(now, quietPeriod), now);
        }
        SchedulePreview(now);
        FlushBufferedTranslation(now);
        ExpireFloatingLines(now);
        ReplayFloatingRemovalTombstones();
    }

    private void HandleFinal(string text, TimeSpan now)
    {
        var normalized = IncrementalSubtitleSegmenter.Normalize(text);
        if (normalized.Length > 0
            && !_hasPartialSinceFinal
            && string.Equals(normalized, _lastFinalText, StringComparison.Ordinal)
            && _lastFinalAt is { } lastFinalAt
            && now - lastFinalAt <= DuplicateFinalWindow)
        {
            _lastFinalAt = now;
            return;
        }

        if (normalized.Length > _lastFinalText.Length
            && !_hasPartialSinceFinal
            && _lastFinalAt is { } suffixFinalAt
            && now - suffixFinalAt <= DuplicateFinalWindow
            && normalized.StartsWith(_lastFinalText, StringComparison.Ordinal)
            && IncrementalSubtitleSegmenter.IsOnlyTerminalSuffix(
                normalized[_lastFinalText.Length..]))
        {
            AppendTerminalPunctuation(normalized[_lastFinalText.Length..], now);
            _lastFinalAt = now;
            return;
        }

        if (normalized.Length > 0
            && IncrementalSubtitleSegmenter.IsOnlyTerminalSuffix(normalized))
        {
            AppendTerminalPunctuation(normalized, now);
            _lastFinalAt = now;
            return;
        }

        ApplySegmentation(_segmenter.ApplyFinal(normalized, now), now);
        _lastFinalText = normalized;
        _lastFinalAt = now;
        _hasPartialSinceFinal = false;
    }

    private void ApplySegmentation(SubtitleSegmentationUpdate update, TimeSpan now)
    {
        if (update.ReconcileFinal)
        {
            ReconcileFinalHypothesis(update.Hypothesis, update.PreviousHypothesis, now);
            return;
        }


        if (update.StartsNewUtterance)
            CompleteUtterance();

        if (update.Hypothesis.Length > 0)
            _utteranceHypothesis = update.Hypothesis;

        var settings = _getSettings();
        var bufferAiTranslation = UsesBufferedAiTranslation(settings);
        var maximumSentences = bufferAiTranslation
            ? int.MaxValue
            : Math.Max(1, settings.MaxSentencesPerLine);
        foreach (var commit in update.Commits)
        {
            var start = commit.SourceStart >= 0
                ? commit.SourceStart
                : _currentRange?.Start ?? 0;
            var line = EnsureCurrentLine(now, start);
            var range = _currentRange!;
            range.End = commit.SourceEnd >= 0
                ? Math.Clamp(commit.SourceEnd, range.Start, _utteranceHypothesis.Length)
                : Math.Clamp(start + commit.Text.Length, range.Start, _utteranceHypothesis.Length);
            if (!commit.CloseLine)
                _sentencesInCurrent += Math.Max(1, commit.SentenceCount);
            UpdateSource(line, SliceHypothesis(range), now);
            PublishLine(line);
            var closeForBoundary = commit.CloseLine
                                   && (!bufferAiTranslation
                                       || update.CloseCurrentLine
                                       || IsBufferedAiTranslationFull(line));
            var closeForSentenceLimit = !bufferAiTranslation
                                         && !commit.CloseLine
                                         && _sentencesInCurrent >= maximumSentences;
            if (closeForBoundary
                || closeForSentenceLimit
                || (bufferAiTranslation && IsBufferedAiTranslationFull(line)))
            {
                SealCurrentLine(now);
            }
        }

        if (!string.IsNullOrEmpty(update.AppendToPreviousLine) && _lastUtteranceLine is not null)
        {
            UpdateSource(
                _lastUtteranceLine,
                JoinText(_lastUtteranceLine.OriginalText, update.AppendToPreviousLine),
                now);
            PublishLine(_lastUtteranceLine);
            QueueFinalTranslation(_lastUtteranceLine, now);
        }

        if (update.DraftText.Length > 0)
        {
            var line = EnsureCurrentLine(now, update.DraftStart);
            var range = _currentRange!;
            range.End = _utteranceHypothesis.Length;
            UpdateSource(line, SliceHypothesis(range), now);
            PublishLine(line);
        }
        else if (_currentLine is not null && _currentRange is not null)
        {
            UpdateSource(_currentLine, SliceHypothesis(_currentRange), now);
            PublishLine(_currentLine);
        }

        if (update.CloseCurrentLine)
            SealCurrentLine(now);
        if (update.EndsUtterance)
            CompleteUtterance();
    }

    private ManagedSubtitleLine EnsureCurrentLine(TimeSpan now, int sourceStart)
    {
        if (_currentLine is not null)
            return _currentLine;
        var timestamp = _timestampClock.GetTimestamp();
        _currentLine = new ManagedSubtitleLine(_nextSubtitleId(), timestamp, now);
        ReserveStructuredChildIds(_currentLine);
        _currentRange = new UtteranceLineRange(
            _currentLine,
            Math.Clamp(sourceStart, 0, _utteranceHypothesis.Length),
            Math.Clamp(sourceStart, 0, _utteranceHypothesis.Length));
        _utteranceLines.Add(_currentRange);
        _linesById.Add(_currentLine.Id, _currentLine);
        _floating.Add(_currentLine);
        RegisterFloatingLine(_currentLine, now);
        return _currentLine;
    }

    private string SliceHypothesis(UtteranceLineRange range)
    {
        var start = Math.Clamp(range.Start, 0, _utteranceHypothesis.Length);
        var end = Math.Clamp(range.End, start, _utteranceHypothesis.Length);
        range.Start = IncrementalSubtitleSegmenter.SkipWhitespace(
            _utteranceHypothesis,
            start,
            end);
        range.End = IncrementalSubtitleSegmenter.TrimTrailingWhitespace(
            _utteranceHypothesis,
            range.Start,
            end);
        range.Line.SourceStart = range.Start;
        range.Line.SourceEnd = range.End;
        return _utteranceHypothesis[range.Start..range.End];
    }

    private void UpdateSource(
        ManagedSubtitleLine line,
        string text,
        TimeSpan now)
    {
        text = IncrementalSubtitleSegmenter.Normalize(text);
        if (string.Equals(line.OriginalText, text, StringComparison.Ordinal))
            return;

        var previous = line.OriginalText;
        line.OriginalText = text;
        line.Revision++;
        line.LastSourceChangedAt = now;
        line.ExpiresAt = null;
        line.IsTranslationTerminal = false;
        line.TranslationDefinition = null;
        line.StructuredPlan = null;
        line.StructuredPlanSource = string.Empty;
        line.StructuredPlanDefinition = null;
        if (IsPreviewEligible(text)
            && !string.Equals(line.LastPreviewRequestedSource, text, StringComparison.Ordinal))
        {
            line.PreviewEligibleAt ??= now;
        }
        else
            line.PreviewEligibleAt = null;

        if (_activeTranslation is { IsFinal: false } active
            && active.LineId == line.Id
            && !text.StartsWith(active.SourceText, StringComparison.Ordinal))
        {
            CancelAndDetachActiveTranslation();
        }
        if (_pendingPreview is not null
            && _pendingPreview.LineId == line.Id
            && !text.StartsWith(_pendingPreview.SourceText, StringComparison.Ordinal))
        {
            CancelPendingPreview();
        }

        if (!text.StartsWith(previous, StringComparison.Ordinal))
        {
            line.LastPreviewRequestedSource = string.Empty;
            line.ShadowTranslation = string.Empty;
            line.ShadowTranslationSource = string.Empty;
            line.ShadowTranslationDefinition = null;
            if (line.LastTranslatedSource.Length > 0
                && !text.StartsWith(line.LastTranslatedSource, StringComparison.Ordinal))
            {
                line.LastTranslatedSource = string.Empty;
                line.LastTranslationDefinition = null;
            }
        }
    }

    private void SealCurrentLine(TimeSpan now)
    {
        var line = _currentLine;
        if (line is null)
            return;
        if (string.IsNullOrWhiteSpace(line.OriginalText))
        {
            if (_currentRange is not null)
                _utteranceLines.Remove(_currentRange);
            _currentLine = null;
            _currentRange = null;
            _sentencesInCurrent = 0;
            return;
        }

        line.IsSealed = true;
        line.IsTemporary = false;
        line.PreviewEligibleAt = null;
        if (!_sealedLines.Contains(line))
            _sealedLines.Add(line);
        _lastUtteranceLine = line;
        _currentLine = null;
        _currentRange = null;
        _sentencesInCurrent = 0;
        PublishLine(line);
        QueueFinalTranslation(line, now);
    }

    private void CompleteUtterance()
    {
        if (_utteranceLines.Count > 0)
            _lastUtteranceLine = _utteranceLines[^1].Line;
        _utteranceLines.Clear();
        _utteranceHypothesis = string.Empty;
        _currentLine = null;
        _currentRange = null;
        _sentencesInCurrent = 0;
    }

    private void AppendTerminalPunctuation(string punctuation, TimeSpan now)
    {
        var target = _currentLine
                     ?? _utteranceLines.LastOrDefault()?.Line
                     ?? _lastUtteranceLine;
        if (target is null)
        {
            _segmenter.Reset();
            _lastFinalText = punctuation;
            _hasPartialSinceFinal = false;
            return;
        }

        var overlap = FindSuffixOverlap(target.OriginalText, punctuation);
        var append = punctuation[overlap..];
        if (append.Length > 0)
        {
            UpdateSource(target, target.OriginalText + append, now);
            target.SourceEnd += append.Length;
        }

        if (ReferenceEquals(target, _currentLine))
        {
            target.IsSourceFinalized = true;
            SealCurrentLine(now);
        }
        else
        {
            target.IsSourceFinalized = true;
            target.IsSealed = true;
            target.IsTemporary = false;
            target.PreviewEligibleAt = null;
            if (!_sealedLines.Contains(target))
                _sealedLines.Add(target);
            PublishLine(target);
            QueueFinalTranslation(target, now);
        }

        _lastUtteranceLine = target;
        _lastFinalText = target.OriginalText;
        _hasPartialSinceFinal = false;
        _segmenter.Reset();
        CompleteUtterance();
    }

    private static int FindSuffixOverlap(string text, string suffix)
    {
        for (var length = Math.Min(text.Length, suffix.Length); length > 0; length--)
        {
            if (text.EndsWith(suffix[..length], StringComparison.Ordinal))
                return length;
        }
        return 0;
    }

    private void ReconcileFinalHypothesis(
        string finalHypothesis,
        string previousHypothesis,
        TimeSpan now)
    {
        finalHypothesis = IncrementalSubtitleSegmenter.Normalize(finalHypothesis);
        if (finalHypothesis.Length == 0)
        {
            if (_currentLine is not null)
                _currentLine.IsSourceFinalized = true;
            SealCurrentLine(now);
            CompleteUtterance();
            return;
        }

        var prior = _utteranceHypothesis.Length > 0
            ? _utteranceHypothesis
            : previousHypothesis;
        var commonPrefix = IncrementalSubtitleSegmenter.AlignPrefixToTextElement(
            finalHypothesis,
            FindOrdinalPrefixLength(prior, finalHypothesis));
        var firstAffected = _utteranceLines.FindIndex(range =>
            !range.Line.IsSealed
            || range.End > commonPrefix
            || !RangeMatchesHypothesis(range, finalHypothesis));
        var preservedCount = firstAffected < 0 ? _utteranceLines.Count : firstAffected;
        var preserved = _utteranceLines.Take(preservedCount).ToList();
        var affected = _utteranceLines.Skip(preservedCount).ToList();
        _utteranceHypothesis = finalHypothesis;
        var rebuildStart = preserved.Count == 0
            ? 0
            : IncrementalSubtitleSegmenter.SkipWhitespace(
                finalHypothesis,
                preserved[^1].End,
                finalHypothesis.Length);
        var settings = _getSettings();
        var bufferAiTranslation = UsesBufferedAiTranslation(settings);
        var desired = BuildFinalLineRanges(
            finalHypothesis,
            rebuildStart,
            bufferAiTranslation ? int.MaxValue : Math.Max(1, settings.MaxSentencesPerLine),
            bufferAiTranslation ? AiMaximumWordsPerTranslation : IncrementalSubtitleSegmenter.MaximumWords,
            bufferAiTranslation
                ? AiMaximumDisplayColumnsPerTranslation
                : IncrementalSubtitleSegmenter.MaximumDisplayColumns);
        if (preserved.Count > 0
            && desired.Count == 1
            && IncrementalSubtitleSegmenter.IsOnlyTerminalSuffix(
                finalHypothesis[desired[0].Start..desired[0].End]))
        {
            var lastPreserved = preserved[^1];
            lastPreserved.End = desired[0].End;
            UpdateSource(
                lastPreserved.Line,
                SliceHypothesis(lastPreserved),
                now);
            desired.Clear();
        }

        var rebuilt = new List<UtteranceLineRange>(preserved);
        for (var index = 0; index < desired.Count; index++)
        {
            var source = desired[index];
            UtteranceLineRange binding;
            if (index < affected.Count)
            {
                binding = affected[index];
                binding.Start = source.Start;
                binding.End = source.End;
            }
            else
            {
                var line = CreateLine(now);
                binding = new UtteranceLineRange(line, source.Start, source.End);
            }
            rebuilt.Add(binding);
            UpdateSource(
                binding.Line,
                SliceHypothesis(binding),
                now);
        }

        foreach (var obsolete in affected.Skip(desired.Count))
        {
            RemoveQueuedTranslations(obsolete.Line.Id);
            if (_pendingPreview?.LineId == obsolete.Line.Id)
                CancelPendingPreview();
            if (_activeTranslation?.LineId == obsolete.Line.Id)
                CancelAndDetachActiveTranslation();
            UpdateSource(obsolete.Line, string.Empty, now);
            obsolete.Line.IsSealed = true;
            obsolete.Line.IsTemporary = false;
            obsolete.Line.IsTranslating = false;
            PublishLine(obsolete.Line);
            RemoveFromFloating(obsolete.Line);
            _floating.Remove(obsolete.Line);
            _sealedLines.Remove(obsolete.Line);
            _linesById.Remove(obsolete.Line.Id);
        }

        _utteranceLines.Clear();
        _utteranceLines.AddRange(rebuilt);
        _currentLine = null;
        _currentRange = null;
        _sentencesInCurrent = 0;

        foreach (var binding in rebuilt)
        {
            var line = binding.Line;
            line.IsSourceFinalized = true;
            line.IsSealed = true;
            line.IsTemporary = false;
            line.PreviewEligibleAt = null;
            if (!_sealedLines.Contains(line))
                _sealedLines.Add(line);
            PublishLine(line);
        }
        foreach (var binding in rebuilt)
            QueueFinalTranslation(binding.Line, now);

        _lastUtteranceLine = rebuilt.LastOrDefault()?.Line ?? _lastUtteranceLine;
        _lastFinalText = finalHypothesis;
        _hasPartialSinceFinal = false;
        CompleteUtterance();
    }

    private static int FindOrdinalPrefixLength(string left, string right)
    {
        var length = Math.Min(left.Length, right.Length);
        var index = 0;
        while (index < length && left[index] == right[index])
            index++;
        return index;
    }

    private static bool RangeMatchesHypothesis(
        UtteranceLineRange range,
        string hypothesis) =>
        range.Start >= 0
        && range.End >= range.Start
        && range.End <= hypothesis.Length
        && string.Equals(
            range.Line.OriginalText,
            hypothesis[range.Start..range.End],
            StringComparison.Ordinal);

    private ManagedSubtitleLine CreateLine(TimeSpan now)
    {
        var line = new ManagedSubtitleLine(
            _nextSubtitleId(),
            _timestampClock.GetTimestamp(),
            now);
        ReserveStructuredChildIds(line);
        _linesById.Add(line.Id, line);
        _floating.Add(line);
        RegisterFloatingLine(line, now);
        return line;
    }

    private void ReserveStructuredChildIds(ManagedSubtitleLine line)
    {
        if (!UsesBufferedAiTranslation(_getSettings()) || line.ReservedChildIds.Count > 0)
            return;
        for (var index = 1; index < MaximumStructuredSegments; index++)
            line.ReservedChildIds.Add(_nextSubtitleId());
    }

    private static List<SubtitleSourceRange> BuildFinalLineRanges(
        string text,
        int sourceStart,
        int maximumSentences,
        int maximumWords,
        int maximumDisplayColumns)
    {
        var ranges = new List<SubtitleSourceRange>();
        var lineStart = IncrementalSubtitleSegmenter.SkipWhitespace(
            text,
            sourceStart,
            text.Length);
        var sentences = 0;
        foreach (var boundary in IncrementalSubtitleSegmenter.FindStrongBoundaries(text))
        {
            if (boundary <= lineStart)
                continue;
            sentences++;
            if (sentences < maximumSentences)
                continue;
            AddSizedRanges(
                text,
                lineStart,
                boundary,
                maximumWords,
                maximumDisplayColumns,
                ranges);
            lineStart = IncrementalSubtitleSegmenter.SkipWhitespace(text, boundary, text.Length);
            sentences = 0;
        }
        if (lineStart < text.Length)
            AddSizedRanges(
                text,
                lineStart,
                text.Length,
                maximumWords,
                maximumDisplayColumns,
                ranges);
        return ranges;
    }

    private static void AddSizedRanges(
        string text,
        int start,
        int end,
        int maximumWords,
        int maximumDisplayColumns,
        List<SubtitleSourceRange> destination)
    {
        start = IncrementalSubtitleSegmenter.SkipWhitespace(text, start, end);
        end = IncrementalSubtitleSegmenter.TrimTrailingWhitespace(text, start, end);
        while (start < end)
        {
            var candidate = text[start..end];
            if (IncrementalSubtitleSegmenter.CountWords(candidate)
                    <= maximumWords
                && IncrementalSubtitleSegmenter.CountDisplayColumns(candidate)
                    <= maximumDisplayColumns)
            {
                destination.Add(new SubtitleSourceRange(start, end));
                return;
            }

            var cut = IncrementalSubtitleSegmenter.FindPreferredCut(
                candidate,
                maximumWords,
                maximumDisplayColumns);
            if (cut <= 0 || cut >= candidate.Length)
                cut = Math.Max(1, candidate.Length / 2);
            var pieceEnd = IncrementalSubtitleSegmenter.TrimTrailingWhitespace(
                text,
                start,
                start + cut);
            if (pieceEnd <= start)
                pieceEnd = Math.Min(end, start + cut);
            destination.Add(new SubtitleSourceRange(start, pieceEnd));
            start = IncrementalSubtitleSegmenter.SkipWhitespace(text, pieceEnd, end);
        }
    }

    private void SchedulePreview(TimeSpan now)
    {
        var line = _currentLine;
        var settings = _getSettings();
        if (line is null
            || line.IsSealed
            || !settings.IsTranslationEnabled
            || (settings.EngineType == 0 && !settings.IsRealTimePreviewEnabled))
            return;
        if (!IsPreviewEligible(line.OriginalText) || line.PreviewEligibleAt is null)
            return;
        if (string.Equals(line.LastPreviewRequestedSource, line.OriginalText, StringComparison.Ordinal))
            return;
        var debounce = settings.EngineType == 0
            ? MachinePreviewDebounce
            : AiPreviewDebounce;
        var maximumWait = settings.EngineType == 0
            ? MachinePreviewMaximumWait
            : AiPreviewMaximumWait;
        if (now - line.LastSourceChangedAt < debounce
            && now - line.PreviewEligibleAt.Value < maximumWait)
        {
            return;
        }

        QueuePreviewTranslation(line);
    }

    private void QueuePreviewTranslation(ManagedSubtitleLine line)
    {
        if (_finalJobs.Count > 0 || _recognitionStopped)
            return;
        CancelPendingPreview();
        var definition = CreateTranslationDefinition(line, _getSettings());
        if (_unavailableTranslationSelections.Contains(definition.Selection))
        {
            line.LastPreviewRequestedSource = line.OriginalText;
            line.PreviewEligibleAt = null;
            return;
        }
        if (GetTranslationLane(definition.Selection).IsUnavailable())
            return;
        _pendingPreview = CreateTranslationJob(
            line,
            isFinal: false,
            definition,
            CaptureTranslationDisplay(line));
        line.TranslationDefinition = _pendingPreview.Definition;
        line.LastPreviewRequestedSource = line.OriginalText;
        line.PreviewEligibleAt = null;
        line.IsTranslating = true;
        PublishLine(line);
    }

    private void QueueFinalTranslation(ManagedSubtitleLine line, TimeSpan now)
    {
        var settings = _getSettings();
        var recoveryDisplay = CaptureTranslationDisplay(line);
        if (!settings.IsTranslationEnabled)
        {
            CancelDisabledTranslationWork(now);
            if (line.IsTranslationTerminal)
                return;
            line.TranslationDefinition = null;
            MarkTranslationTerminal(line, now);
            return;
        }

        if (settings.EngineType != 0)
            ReserveStructuredChildIds(line);

        var expectedDefinition = CreateTranslationDefinition(line, settings);
        if (line.IsSourceFinalized
            && TryMaterializeStructuredPlan(line, expectedDefinition, now))
        {
            return;
        }
        if (line.IsTranslationTerminal
            && Equals(line.TranslationDefinition, expectedDefinition))
        {
            return;
        }
        if (_unavailableTranslationSelections.Contains(expectedDefinition.Selection)
            || GetTranslationLane(expectedDefinition.Selection).IsUnavailable())
        {
            line.TranslationDefinition = expectedDefinition;
            MarkTranslationTerminal(line, now);
            return;
        }

        line.IsTranslationTerminal = false;
        line.TranslationDefinition = expectedDefinition;
        line.ExpiresAt = null;
        if (_pendingPreview is { } pending
            && pending.LineId == line.Id
            && pending.Revision == line.Revision
            && Equals(pending.Definition, expectedDefinition)
            && string.Equals(pending.SourceText, line.OriginalText, StringComparison.Ordinal))
        {
            _pendingPreview = null;
            pending.IsFinal = true;
            _finalJobs.AddLast(pending);
            line.IsTranslating = true;
            PublishLine(line);
            return;
        }
        if (_activeTranslation is { } active)
        {
            if (!active.IsObsolete
                && active.LineId == line.Id
                && active.Revision == line.Revision
                && Equals(active.Definition, expectedDefinition)
                && string.Equals(active.SourceText, line.OriginalText, StringComparison.Ordinal))
            {
                if (!active.IsFinal)
                    active.IsFinal = true;
                line.IsTranslating = true;
                PublishLine(line);
                return;
            }
        }

        if (string.Equals(line.ShadowTranslationSource, line.OriginalText, StringComparison.Ordinal)
            && Equals(line.ShadowTranslationDefinition, expectedDefinition)
            && !string.IsNullOrWhiteSpace(line.ShadowTranslation))
        {
            CancelPendingPreview();
            line.TranslatedText = line.ShadowTranslation;
            line.DisplayTranslatedText = line.ShadowTranslation;
            line.LastTranslatedSource = line.OriginalText;
            line.LastTranslationDefinition = expectedDefinition;
            MarkTranslationTerminal(line, now);
            return;
        }
        if (string.Equals(line.LastTranslatedSource, line.OriginalText, StringComparison.Ordinal)
            && Equals(line.LastTranslationDefinition, expectedDefinition)
            && !string.IsNullOrWhiteSpace(line.DisplayTranslatedText))
        {
            CancelPendingPreview();
            MarkTranslationTerminal(line, now);
            return;
        }

        CancelPendingPreview();
        RemoveQueuedTranslations(line.Id);
        _finalJobs.AddLast(CreateTranslationJob(
            line,
            isFinal: true,
            expectedDefinition,
            recoveryDisplay));
        if (_activeTranslation is { } activeToCancel)
        {
            if (activeToCancel.LineId == line.Id || !activeToCancel.IsFinal)
            {
                var preserveReadablePrefix = activeToCancel.LineId == line.Id
                                             && line.OriginalText.StartsWith(
                                                 activeToCancel.SourceText,
                                                 StringComparison.Ordinal)
                                             && !string.IsNullOrWhiteSpace(
                                                 line.DisplayTranslatedText);
                CancelAndDetachActiveTranslation(preserveReadablePrefix);
            }
        }
        line.IsTranslating = true;
        PublishLine(line);
    }

    private TranslationJob CreateTranslationJob(
        ManagedSubtitleLine line,
        bool isFinal,
        TranslationJobDefinition? definition = null,
        TranslationDisplaySnapshot? recoveryDisplay = null,
        int attempt = 1,
        TimeSpan? notBefore = null)
    {
        definition ??= CreateTranslationDefinition(line, _getSettings());
        return new TranslationJob(
            Interlocked.Increment(ref _nextTranslationJobId),
            line.Id,
            line.Revision,
            line.OriginalText,
            isFinal,
            definition,
            CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None),
            recoveryDisplay,
            preserveDisplayUntilCompleted: !string.IsNullOrWhiteSpace(line.DisplayTranslatedText),
            attempt,
            notBefore);
    }

    private static TranslationDisplaySnapshot? CaptureTranslationDisplay(
        ManagedSubtitleLine line)
    {
        if (string.IsNullOrWhiteSpace(line.DisplayTranslatedText)
            || !string.Equals(
                line.LastTranslatedSource,
                line.OriginalText,
                StringComparison.Ordinal))
        {
            return null;
        }

        return new TranslationDisplaySnapshot(
            line.OriginalText,
            line.TranslatedText,
            line.DisplayTranslatedText,
            line.LastTranslationDefinition,
            line.IsTranslationTerminal,
            line.ExpiresAt);
    }

    private TranslationJobDefinition CreateTranslationDefinition(
        ManagedSubtitleLine line,
        SpeechRecognitionSettings settings)
    {
        var isAi = settings.EngineType != 0;
        var lineIndex = _sealedLines.IndexOf(line);
        var contextCandidates = lineIndex >= 0
            ? _sealedLines.Take(lineIndex)
            : _sealedLines;
        var context = contextCandidates
            .TakeLast(2)
            .Select(candidate => new SubtitleTranslationContext(
                candidate.OriginalText,
                string.Equals(
                    candidate.LastTranslatedSource,
                    candidate.OriginalText,
                    StringComparison.Ordinal)
                    ? candidate.DisplayTranslatedText
                    : string.Empty))
            .ToArray();
        var requestText = isAi
            ? JsonSerializer.Serialize(new
            {
                context,
                current = line.OriginalText
            })
            : line.OriginalText;
        var selection = isAi
            ? new TranslationProviderSelection(
                TranslationEngineNames.AiModel,
                AiModelId: settings.EngineId,
                PromptOverride: SubtitlePrompt,
                PromptId: settings.PromptId)
            : new TranslationProviderSelection(
                TranslationEngineNames.MachineTrans,
                MachineProviderId: settings.EngineId);
        return new TranslationJobDefinition(
            requestText,
            selection,
            _languages.Get(MapRecognitionLanguage(settings.RecognitionLanguage)),
            _languages.Get(settings.TargetLanguage));
    }

    private void TryStartTranslation(CancellationToken sessionToken)
    {
        if (!_getSettings().IsTranslationEnabled)
        {
            CancelDisabledTranslationWork(GetMonotonicNow());
            return;
        }
        if (_activeTranslation is not null)
            return;
        while (true)
        {
            TranslationJob? job = null;
            if (_finalJobs.First is not null)
            {
                job = _finalJobs.First.Value;
                if (job.NotBefore is { } notBefore && GetMonotonicNow() < notBefore)
                    return;
                _finalJobs.RemoveFirst();
            }
            else if (!_recognitionStopped && _pendingPreview is not null)
            {
                job = _pendingPreview;
                _pendingPreview = null;
            }
            if (job is null)
                return;
            if (_unavailableTranslationSelections.Contains(job.Selection)
                || GetTranslationLane(job.Selection).IsUnavailable())
            {
                RejectUnavailableTranslationJob(job, GetMonotonicNow());
                continue;
            }

            job.SessionRegistration = sessionToken.Register(job.Cancellation.Cancel);
            _activeTranslation = job;
            job.Runner = RunTranslationAsync(job, sessionToken);
            return;
        }
    }

    private void RejectUnavailableTranslationJob(TranslationJob job, TimeSpan now)
    {
        job.Cancellation.Dispose();
        if (!_linesById.TryGetValue(job.LineId, out var line))
            return;
        if (job.IsFinal)
        {
            if (line.Revision == job.Revision
                && string.Equals(line.OriginalText, job.SourceText, StringComparison.Ordinal)
                && (!line.IsTranslationTerminal || line.IsTranslating))
            {
                MarkTranslationTerminal(line, now);
            }
            return;
        }
        if (line.IsTranslating)
        {
            line.IsTranslating = false;
            PublishLine(line);
        }
    }

    private async Task RunTranslationAsync(TranslationJob job, CancellationToken sessionToken)
    {
        using var logicalLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            sessionToken,
            job.Cancellation.Token);
        var translationLane = GetTranslationLane(job.Selection);
        translationLane.RegisterProviderRun(job.ProviderRunKey, job.Selection);
        var providerRun = RunProviderTranslationAsync(job, sessionToken);
        try
        {
            var timeout = string.Equals(
                job.Selection.Engine,
                TranslationEngineNames.MachineTrans,
                StringComparison.Ordinal)
                ? MachineTranslationTimeout
                : AiTranslationTimeout;
            var result = await providerRun.WaitAsync(
                    timeout,
                    _timeProvider,
                    logicalLifetime.Token)
                .ConfigureAwait(false);
            await TryWriteCompletionAsync(
                CreateTranslationCompletion(job, result),
                sessionToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            translationLane.MarkTimedOut(job.ProviderRunKey);
            TryCancel(job.Cancellation);
            var providerStopped = await WaitForProviderExitAfterCancellationAsync(
                    providerRun,
                    sessionToken)
                .ConfigureAwait(false);
            var canRetry = providerStopped
                           && !translationLane.IsUnavailable()
                           && !string.Equals(
                               job.Selection.Engine,
                               TranslationEngineNames.MachineTrans,
                               StringComparison.Ordinal);
            Exception completionException = canRetry
                ? new TimeoutException("Subtitle translation timed out.", exception)
                : new OperationCanceledException("Subtitle translation timed out.", exception);
            await TryWriteCompletionAsync(
                new TranslationCompletedMessage(
                    job.Id,
                    job.LineId,
                    job.Revision,
                    job.SourceText,
                    string.Empty,
                    completionException,
                    false,
                    false),
                sessionToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (logicalLifetime.IsCancellationRequested)
        {
            await TryWriteCompletionAsync(
                new TranslationCompletedMessage(
                    job.Id,
                    job.LineId,
                    job.Revision,
                    job.SourceText,
                    string.Empty,
                    null,
                    true,
                    false),
                sessionToken).ConfigureAwait(false);
        }
        finally
        {
            job.SessionRegistration.Dispose();
            job.Cancellation.Dispose();
        }
    }

    private async Task<TranslationRunResult> RunProviderTranslationAsync(
        TranslationJob job,
        CancellationToken sessionToken)
    {
        using var providerLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            sessionToken,
            job.Cancellation.Token);
        var translationLane = GetTranslationLane(job.Selection);
        var gateHeld = false;
        var wasStructured = false;
        try
        {
            var session = _translation.Prepare(job.Selection);
            using var disposable = session as IDisposable;
            var structured = session as IStructuredJsonLinesTranslationSession;
            if (structured is not null
                && !string.Equals(
                    job.Selection.Engine,
                    TranslationEngineNames.MachineTrans,
                    StringComparison.Ordinal))
            {
                wasStructured = true;
                await _inbox.Writer.WriteAsync(
                        new StructuredTranslationStartedMessage(
                            job.Id,
                            job.LineId,
                            job.Revision,
                            job.SourceText),
                        providerLifetime.Token)
                    .ConfigureAwait(false);
            }

            await translationLane.WaitAsync(job.ProviderRunKey, providerLifetime.Token)
                .ConfigureAwait(false);
            gateHeld = true;
            var builder = new StringBuilder();
            if (structured is not null && wasStructured)
            {
                await foreach (var item in structured.StreamJsonLinesAsync(
                                   new TranslationRequest(
                                       job.RequestText,
                                       job.SourceLanguage,
                                       job.TargetLanguage,
                                       Provider: job.Selection),
                                   StructuredSubtitleContract,
                                   providerLifetime.Token).ConfigureAwait(false))
                {
                    await _inbox.Writer.WriteAsync(
                            new StructuredTranslationSegmentMessage(
                                job.Id,
                                job.LineId,
                                job.Revision,
                                job.SourceText,
                                item),
                            providerLifetime.Token)
                        .ConfigureAwait(false);
                }
                return new TranslationRunResult(string.Empty, null, false, true);
            }

            await foreach (var item in session.StreamAsync(
                               new TranslationRequest(
                                   job.RequestText,
                                   job.SourceLanguage,
                                   job.TargetLanguage,
                                   Provider: job.Selection),
                               providerLifetime.Token).ConfigureAwait(false))
            {
                switch (item)
                {
                    case TranslationDeltaEvent { Text.Length: > 0 } delta:
                        builder.Append(delta.Text);
                        await _inbox.Writer.WriteAsync(
                                new TranslationBufferMessage(
                                    job.Id,
                                    job.LineId,
                                    job.Revision,
                                    job.SourceText,
                                    builder.ToString()),
                                providerLifetime.Token)
                            .ConfigureAwait(false);
                        break;
                    case TranslationFailedEvent failed:
                        throw new InvalidOperationException(failed.Error.Message);
                }
            }
            return new TranslationRunResult(builder.ToString(), null, false, false);
        }
        catch (OperationCanceledException) when (providerLifetime.IsCancellationRequested)
        {
            return new TranslationRunResult(string.Empty, null, true, wasStructured);
        }
        catch (Exception exception)
        {
            return new TranslationRunResult(string.Empty, exception, false, wasStructured);
        }
        finally
        {
            translationLane.CompleteProviderRun(job.ProviderRunKey, gateHeld);
        }
    }

    private static TranslationCompletedMessage CreateTranslationCompletion(
        TranslationJob job,
        TranslationRunResult result) =>
        new(
            job.Id,
            job.LineId,
            job.Revision,
            job.SourceText,
            result.Text,
            result.Exception,
            result.WasCanceled,
            result.WasStructured);

    private void HandleTranslationBuffer(TranslationBufferMessage message, TimeSpan now)
    {
        var job = _activeTranslation;
        if (job is null
            || job.IsObsolete
            || !MessageMatchesJob(message, job)
            || !TryResolveJobLine(job, out var line))
            return;
        job.Buffer = message.Text;
        line.ShadowTranslation = message.Text;
        line.ShadowTranslationSource = job.SourceText;
        line.ShadowTranslationDefinition = job.Definition;
        if (job.PreserveDisplayUntilCompleted)
            return;
        if (!job.Revealed
            && IsReadableTranslation(message.Text)
            && CanRevealTranslation(line, job))
        {
            job.Revealed = true;
            ApplyTranslationDisplay(line, job, message.Text);
            job.NextDisplayAt = now + DisplayUpdateInterval;
        }
        else if (job.Revealed && now >= job.NextDisplayAt)
        {
            ApplyTranslationDisplay(line, job, message.Text);
            job.NextDisplayAt = now + DisplayUpdateInterval;
        }
    }

    private void HandleStructuredTranslationStarted(
        StructuredTranslationStartedMessage message)
    {
        var job = _activeTranslation;
        if (job is null
            || job.IsObsolete
            || !MessageMatchesJob(message, job))
        {
            return;
        }
        job.IsStructured = true;
    }

    private void HandleTranslationCompleted(TranslationCompletedMessage message, TimeSpan now)
    {
        var job = _activeTranslation;
        if (job is null || !MessageMatchesJob(message, job))
            return;
        _activeTranslation = null;

        if (job.IsObsolete)
            return;

        if (TryResolveJobLine(job, out var line))
        {
            if (message.Exception is not null
                && TryQueueFinalTranslationRetry(line, job, now, message.Exception))
            {
                return;
            }

            var structuredAttempted = job.IsStructured
                                      || message.WasStructured
                                      || job.StructuredPlanBuilder is not null;
            var structuredSucceeded = false;
            if (message.Exception is not null)
            {
                if (message.Exception is OperationCanceledException)
                    _logger.LogDebug("Subtitle translation timed out for line {SubtitleId}.", line.Id);
                else
                    _logger.LogError(message.Exception, "Subtitle translation failed for line {SubtitleId}.", line.Id);
            }
            else if (!message.WasCanceled)
            {
                var translated = message.Text.Length > 0 ? message.Text : job.Buffer;
                if (structuredAttempted
                    && job.StructuredPlanBuilder is not null
                    && job.StructuredPlanBuilder.TryComplete(out var structuredPlan))
                {
                    structuredSucceeded = true;
                    line.ShadowTranslation = translated;
                    line.ShadowTranslationSource = job.SourceText;
                    line.ShadowTranslationDefinition = job.Definition;
                    line.StructuredPlan = structuredPlan;
                    line.StructuredPlanSource = job.SourceText;
                    line.StructuredPlanDefinition = job.Definition;
                }
                else if (structuredAttempted)
                {
                    if (TryQueueFinalTranslationRetry(line, job, now))
                        return;
                    _logger.LogWarning(
                        "Ignoring an invalid structured subtitle plan for line {SubtitleId}.",
                        line.Id);
                }
                else
                {
                    line.ShadowTranslation = translated;
                    line.ShadowTranslationSource = job.SourceText;
                    line.ShadowTranslationDefinition = job.Definition;
                }
                if ((!structuredAttempted || structuredSucceeded)
                    && (job.PreserveDisplayUntilCompleted || CanRevealTranslation(line, job)))
                {
                    ApplyTranslationDisplay(
                        line,
                        job,
                        translated,
                        publish: !job.PreserveDisplayUntilCompleted);
                }
            }

            var restoredTerminalDisplay = structuredAttempted
                                          && !structuredSucceeded
                                          && RollbackStructuredTranslation(line, job);

            if (job.IsFinal && line.IsSealed
                            && string.Equals(line.OriginalText, job.SourceText, StringComparison.Ordinal))
            {
                var materializedStructuredPlan = structuredSucceeded
                                                 && line.IsSourceFinalized
                                                 && TryMaterializeStructuredPlan(
                                                     line,
                                                     job.Definition,
                                                     now);
                if (!materializedStructuredPlan && restoredTerminalDisplay)
                {
                    PublishLine(line);
                }
                else if (!materializedStructuredPlan)
                {
                    MarkTranslationTerminal(line, now);
                }
            }
            else
            {
                line.IsTranslating = false;
                PublishLine(line);
            }
        }

        if (message.Exception is OperationCanceledException)
            DisableTimedOutTranslationSelection(job.Selection, now);
    }

    private bool TryQueueFinalTranslationRetry(
        ManagedSubtitleLine line,
        TranslationJob job,
        TimeSpan now,
        Exception? exception = null)
    {
        var maximumAttempts = GetMaximumFinalTranslationAttempts(exception);
        if (!job.IsFinal
            || job.Attempt >= maximumAttempts)
        {
            return false;
        }

        var preserveReadableJobOutput = job.RecoveryDisplay is null
                                        && !string.IsNullOrWhiteSpace(
                                            line.DisplayTranslatedText)
                                        && string.Equals(
                                            line.LastTranslatedSource,
                                            job.SourceText,
                                            StringComparison.Ordinal);
        if (!preserveReadableJobOutput)
            RollbackStructuredTranslation(line, job);
        line.IsTranslationTerminal = false;
        line.TranslationDefinition = job.Definition;
        line.ExpiresAt = null;
        line.IsTranslating = true;
        var nextAttempt = job.Attempt + 1;
        _finalJobs.AddFirst(CreateTranslationJob(
            line,
            isFinal: true,
            job.Definition,
            job.RecoveryDisplay,
            attempt: nextAttempt,
            notBefore: now + GetFinalTranslationRetryDelay(nextAttempt)));
        if (exception is null)
        {
            _logger.LogWarning(
                "Retrying invalid structured subtitle plan for line {SubtitleId} (attempt {Attempt}/{MaximumAttempts}).",
                line.Id,
                nextAttempt,
                maximumAttempts);
        }
        else
        {
            _logger.LogWarning(
                exception,
                "Retrying transient subtitle translation failure for line {SubtitleId} (attempt {Attempt}/{MaximumAttempts}).",
                line.Id,
                nextAttempt,
                maximumAttempts);
        }
        PublishLine(line);
        return true;
    }

    internal static TimeSpan GetFinalTranslationRetryDelay(int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 2);
        var exponent = Math.Min(attempt - 2, 2);
        return TimeSpan.FromTicks(FinalTranslationRetryDelay.Ticks * (1L << exponent));
    }

    private static int GetMaximumFinalTranslationAttempts(Exception? exception)
    {
        if (exception is null)
            return MaximumInvalidStructuredPlanAttempts;
        if (exception is TimeoutException)
            return MaximumTimedOutTranslationAttempts;
        return IsTransientTranslationFailure(exception)
            ? MaximumTransientTranslationAttempts
            : 0;
    }

    private static bool IsTransientTranslationFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException)
                return false;
        }

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException or IOException)
                return true;
            if (current is HttpRequestException { StatusCode: null })
                return true;
            if (TryGetHttpStatusCode(current) is { } status)
            {
                return status is 0 or 408 or 409 or 425 or 429
                       || status is >= 500 and <= 599;
            }
        }

        return false;
    }

    private static int? TryGetHttpStatusCode(Exception exception)
    {
        if (exception is HttpRequestException { StatusCode: { } statusCode })
            return (int)statusCode;

        try
        {
            var statusProperty = exception.GetType().GetProperty("Status");
            return statusProperty?.GetIndexParameters().Length == 0
                   && statusProperty.GetValue(exception) is int status
                ? status
                : null;
        }
        catch (Exception reflectionException) when (reflectionException is
                   AmbiguousMatchException
                   or MethodAccessException
                   or TargetException
                   or TargetInvocationException)
        {
            return null;
        }
    }

    private static async Task<bool> WaitForProviderExitAfterCancellationAsync(
        Task providerRun,
        CancellationToken sessionToken)
    {
        var gracePeriod = Task.Delay(ProviderCancellationGracePeriod, sessionToken);
        if (!ReferenceEquals(
                await Task.WhenAny(providerRun, gracePeriod).ConfigureAwait(false),
                providerRun))
        {
            return false;
        }

        try
        {
            await providerRun.ConfigureAwait(false);
        }
        catch
        {
            // The provider run owns error reporting; this wait only confirms lane release.
        }
        return true;
    }

    private void DisableTimedOutTranslationSelection(
        TranslationProviderSelection selection,
        TimeSpan now)
    {
        _unavailableTranslationSelections.Add(selection);
        if (_pendingPreview is { } pending
            && Equals(pending.Selection, selection))
        {
            CancelPendingPreview();
        }
        var node = _finalJobs.First;
        while (node is not null)
        {
            var next = node.Next;
            var queued = node.Value;
            if (Equals(queued.Selection, selection))
            {
                _finalJobs.Remove(node);
                queued.Cancellation.Dispose();
                if (_linesById.TryGetValue(queued.LineId, out var line)
                    && line.Revision == queued.Revision
                    && string.Equals(line.OriginalText, queued.SourceText, StringComparison.Ordinal))
                {
                    MarkTranslationTerminal(line, now);
                }
            }
            node = next;
        }
    }

    private void HandleStructuredTranslationSegment(
        StructuredTranslationSegmentMessage message,
        TimeSpan now)
    {
        var job = _activeTranslation;
        if (job is null
            || job.IsObsolete
            || !MessageMatchesJob(message, job)
            || !TryResolveJobLine(job, out var line))
        {
            return;
        }

        job.StructuredPlanBuilder ??= new JsonLinesSubtitlePlanBuilder(job.SourceText);
        if (!job.StructuredPlanBuilder.TryAdd(message.Item, out _))
            return;

        var segmentTranslation = message.Item.GetProperty("translation").GetString()!;
        job.Buffer = JoinText(job.Buffer, segmentTranslation);
        line.ShadowTranslation = job.Buffer;
        line.ShadowTranslationSource = job.SourceText;
        line.ShadowTranslationDefinition = job.Definition;
        if (job.PreserveDisplayUntilCompleted)
            return;
        if (!job.Revealed
            && IsReadableTranslation(job.Buffer)
            && CanRevealTranslation(line, job))
        {
            job.Revealed = true;
            ApplyTranslationDisplay(line, job, job.Buffer);
            job.NextDisplayAt = now + DisplayUpdateInterval;
        }
        else if (job.Revealed && now >= job.NextDisplayAt)
        {
            ApplyTranslationDisplay(line, job, job.Buffer);
            job.NextDisplayAt = now + DisplayUpdateInterval;
        }
    }

    private void FlushBufferedTranslation(TimeSpan now)
    {
        var job = _activeTranslation;
        if (job is null
            || job.PreserveDisplayUntilCompleted
            || !job.Revealed
            || now < job.NextDisplayAt
            || job.Buffer.Length == 0)
        {
            return;
        }
        if (!TryResolveJobLine(job, out var line))
            return;
        ApplyTranslationDisplay(line, job, job.Buffer);
        job.NextDisplayAt = now + DisplayUpdateInterval;
    }

    private bool TryResolveJobLine(TranslationJob job, out ManagedSubtitleLine line)
    {
        line = default!;
        if (!_linesById.TryGetValue(job.LineId, out var candidate))
            return false;
        var sourceMatches = job.IsFinal
            ? candidate.Revision == job.Revision
              && string.Equals(candidate.OriginalText, job.SourceText, StringComparison.Ordinal)
            : candidate.OriginalText.StartsWith(job.SourceText, StringComparison.Ordinal);
        if (!sourceMatches)
            return false;
        line = candidate;
        return true;
    }

    private static bool MessageMatchesJob(
        TranslationBufferMessage message,
        TranslationJob job) =>
        message.JobId == job.Id
        && message.LineId == job.LineId
        && message.Revision == job.Revision
        && string.Equals(message.SourceText, job.SourceText, StringComparison.Ordinal);

    private static bool MessageMatchesJob(
        StructuredTranslationStartedMessage message,
        TranslationJob job) =>
        message.JobId == job.Id
        && message.LineId == job.LineId
        && message.Revision == job.Revision
        && string.Equals(message.SourceText, job.SourceText, StringComparison.Ordinal);

    private static bool MessageMatchesJob(
        StructuredTranslationSegmentMessage message,
        TranslationJob job) =>
        message.JobId == job.Id
        && message.LineId == job.LineId
        && message.Revision == job.Revision
        && string.Equals(message.SourceText, job.SourceText, StringComparison.Ordinal);

    private static bool MessageMatchesJob(
        TranslationCompletedMessage message,
        TranslationJob job) =>
        message.JobId == job.Id
        && message.LineId == job.LineId
        && message.Revision == job.Revision
        && string.Equals(message.SourceText, job.SourceText, StringComparison.Ordinal);

    private void ApplyTranslationDisplay(
        ManagedSubtitleLine line,
        TranslationJob job,
        string text,
        bool publish = true)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        line.TranslatedText = text;
        line.DisplayTranslatedText = text;
        line.ShadowTranslation = text;
        line.ShadowTranslationSource = job.SourceText;
        line.ShadowTranslationDefinition = job.Definition;
        line.LastTranslatedSource = job.SourceText;
        line.LastTranslationDefinition = job.Definition;
        if (publish)
            PublishLine(line);
    }

    private static bool RollbackStructuredTranslation(
        ManagedSubtitleLine line,
        TranslationJob job)
    {
        var recovery = job.RecoveryDisplay;
        if (recovery is not null
            && string.Equals(line.OriginalText, recovery.SourceText, StringComparison.Ordinal))
        {
            line.TranslatedText = recovery.TranslatedText;
            line.DisplayTranslatedText = recovery.DisplayTranslatedText;
            line.ShadowTranslation = recovery.DisplayTranslatedText;
            line.ShadowTranslationSource = recovery.SourceText;
            line.ShadowTranslationDefinition = recovery.Definition;
            line.LastTranslatedSource = recovery.SourceText;
            line.LastTranslationDefinition = recovery.Definition;
            line.TranslationDefinition = recovery.Definition;
            line.IsTranslationTerminal = recovery.IsTranslationTerminal;
            line.ExpiresAt = recovery.ExpiresAt;
            line.IsTranslating = false;
            return recovery.IsTranslationTerminal;
        }

        if (job.PreserveDisplayUntilCompleted)
        {
            line.ShadowTranslation = string.Empty;
            line.ShadowTranslationSource = string.Empty;
            line.ShadowTranslationDefinition = null;
            line.LastTranslatedSource = string.Empty;
            line.LastTranslationDefinition = null;
            line.TranslationDefinition = string.Equals(
                line.OriginalText,
                job.SourceText,
                StringComparison.Ordinal)
                ? job.Definition
                : null;
            line.ExpiresAt = null;
            line.IsTranslating = false;
            return false;
        }

        line.TranslatedText = string.Empty;
        line.DisplayTranslatedText = string.Empty;
        line.ShadowTranslation = string.Empty;
        line.ShadowTranslationSource = string.Empty;
        line.ShadowTranslationDefinition = null;
        line.LastTranslatedSource = string.Empty;
        line.LastTranslationDefinition = null;
        line.TranslationDefinition = job.Definition;
        line.ExpiresAt = null;
        line.IsTranslating = false;
        return false;
    }

    private static bool CanRevealTranslation(ManagedSubtitleLine line, TranslationJob job) =>
        job.IsFinal
        || string.IsNullOrWhiteSpace(line.DisplayTranslatedText)
        || (line.LastTranslatedSource.Length > 0
            && job.SourceText.StartsWith(line.LastTranslatedSource, StringComparison.Ordinal));

    private bool TryMaterializeStructuredPlan(
        ManagedSubtitleLine anchor,
        TranslationJobDefinition definition,
        TimeSpan now)
    {
        var plan = anchor.StructuredPlan;
        if (plan is null
            || !anchor.IsSourceFinalized
            || !string.Equals(anchor.StructuredPlanSource, anchor.OriginalText, StringComparison.Ordinal)
            || !Equals(anchor.StructuredPlanDefinition, definition))
        {
            return false;
        }

        if (plan.Segments.Length == 0
            || plan.Segments.Length > MaximumStructuredSegments
            || plan.Segments.Length - 1 > anchor.ReservedChildIds.Count)
        {
            _logger.LogWarning(
                "Keeping aggregate subtitle line {SubtitleId} because its structured plan has {SegmentCount} segments.",
                anchor.Id,
                plan.Segments.Length);
            anchor.StructuredPlan = null;
            return false;
        }

        var settings = _getSettings();
        var expirySeconds = settings.AutoClearInterval;
        TimeSpan? expiresAt = expirySeconds > 0
            ? now + TimeSpan.FromSeconds(expirySeconds)
            : null;
        var sourceCursor = anchor.SourceStart;
        var materialized = new List<ManagedSubtitleLine>(plan.Segments.Length);
        for (var index = 0; index < plan.Segments.Length; index++)
        {
            var segment = plan.Segments[index];
            var line = index == 0
                ? anchor
                : new ManagedSubtitleLine(
                    anchor.ReservedChildIds[index - 1],
                    anchor.Timestamp,
                    now);
            if (index > 0)
                _linesById.Add(line.Id, line);

            if (!string.Equals(line.OriginalText, segment.Source, StringComparison.Ordinal))
                line.Revision++;
            line.OriginalText = segment.Source;
            line.TranslatedText = segment.Translation;
            line.DisplayTranslatedText = segment.Translation;
            line.ShadowTranslation = segment.Translation;
            line.ShadowTranslationSource = segment.Source;
            line.ShadowTranslationDefinition = definition;
            line.LastTranslatedSource = segment.Source;
            line.LastTranslationDefinition = definition;
            line.TranslationDefinition = definition;
            line.StructuredPlan = null;
            line.StructuredPlanSource = string.Empty;
            line.StructuredPlanDefinition = null;
            line.IsTranslating = false;
            line.IsTranslationTerminal = true;
            line.IsTemporary = false;
            line.IsSealed = true;
            line.IsSourceFinalized = true;
            line.PreviewEligibleAt = null;
            line.ExpiresAt = expiresAt;
            line.SourceStart = sourceCursor;
            sourceCursor += segment.Source.Length;
            line.SourceEnd = sourceCursor;
            materialized.Add(line);
        }

        var sealedIndex = _sealedLines.IndexOf(anchor);
        if (sealedIndex < 0)
        {
            _sealedLines.Add(anchor);
            sealedIndex = _sealedLines.Count - 1;
        }
        _sealedLines.InsertRange(sealedIndex + 1, materialized.Skip(1));

        var floatingIndex = _floating.IndexOf(anchor);
        if (floatingIndex < 0)
        {
            _floating.Add(anchor);
            floatingIndex = _floating.Count - 1;
        }
        _floating.InsertRange(floatingIndex + 1, materialized.Skip(1));

        var utteranceIndex = _utteranceLines.FindIndex(range => ReferenceEquals(range.Line, anchor));
        if (utteranceIndex >= 0)
        {
            _utteranceLines.RemoveAt(utteranceIndex);
            _utteranceLines.InsertRange(
                utteranceIndex,
                materialized.Select(line => new UtteranceLineRange(
                    line,
                    line.SourceStart,
                    line.SourceEnd)));
        }
        if (ReferenceEquals(_lastUtteranceLine, anchor))
            _lastUtteranceLine = materialized[^1];

        var removals = _floatingLifecycle.Materialize(
            anchor.Id,
            materialized.Skip(1).Select(line => line.Id).ToArray(),
            expiresAt,
            now,
            settings.FloatingDisplayMode,
            settings.MaxFloatingHistory);
        foreach (var line in materialized)
            line.IsFloatingVisible = _floatingLifecycle.IsVisible(line.Id);
        ApplyFloatingRemovals(removals);
        foreach (var line in materialized)
            _publish(new SpeechSubtitleChangedEvent(line.Snapshot()));
        return true;
    }

    private void MarkTranslationTerminal(ManagedSubtitleLine line, TimeSpan now)
    {
        line.IsTranslating = false;
        line.IsTranslationTerminal = true;
        var seconds = _getSettings().AutoClearInterval;
        line.ExpiresAt = seconds > 0 ? now + TimeSpan.FromSeconds(seconds) : null;
        PublishLine(line);
    }

    private void ExpireFloatingLines(TimeSpan now)
    {
        var settings = _getSettings();
        ApplyFloatingRemovals(_floatingLifecycle.Sweep(
            now,
            settings.FloatingDisplayMode,
            settings.MaxFloatingHistory));
    }

    private void TrimFloatingHistory()
    {
        var settings = _getSettings();
        ApplyFloatingRemovals(_floatingLifecycle.Sweep(
            GetMonotonicNow(),
            settings.FloatingDisplayMode,
            settings.MaxFloatingHistory));
    }

    private void RemoveFromFloating(ManagedSubtitleLine line)
    {
        line.IsFloatingVisible = false;
        ApplyFloatingRemovals(_floatingLifecycle.Remove(line.Id));
    }

    private void PublishLine(ManagedSubtitleLine line)
    {
        var settings = _getSettings();
        var removals = _floatingLifecycle.Update(
            line.Id,
            line.IsSealed,
            line.IsTranslationTerminal,
            line.ExpiresAt,
            GetMonotonicNow(),
            settings.FloatingDisplayMode,
            settings.MaxFloatingHistory);
        line.IsFloatingVisible = _floatingLifecycle.IsVisible(line.Id);
        _publish(new SpeechSubtitleChangedEvent(line.Snapshot()));
        ApplyFloatingRemovals(removals);
    }

    private void RegisterFloatingLine(ManagedSubtitleLine line, TimeSpan now)
    {
        var settings = _getSettings();
        var removals = _floatingLifecycle.Update(
            line.Id,
            line.IsSealed,
            line.IsTranslationTerminal,
            line.ExpiresAt,
            now,
            settings.FloatingDisplayMode,
            settings.MaxFloatingHistory);
        line.IsFloatingVisible = _floatingLifecycle.IsVisible(line.Id);
        ApplyFloatingRemovals(removals);
    }

    private void ApplyFloatingRemovals(IEnumerable<long> subtitleIds)
    {
        foreach (var subtitleId in subtitleIds)
        {
            if (!_announcedFloatingRemovals.Add(subtitleId))
                continue;
            if (_linesById.TryGetValue(subtitleId, out var line))
                line.IsFloatingVisible = false;
            _publish(new SpeechFloatingSubtitleRemovedEvent(subtitleId));
        }
    }

    private void ReplayFloatingRemovalTombstones() =>
        ApplyFloatingRemovals(_floatingLifecycle.GetRemovalTombstones());

    private void RemoveQueuedTranslations(long lineId)
    {
        var node = _finalJobs.First;
        while (node is not null)
        {
            var next = node.Next;
            if (node.Value.LineId == lineId)
            {
                node.Value.Cancellation.Dispose();
                _finalJobs.Remove(node);
            }
            node = next;
        }
    }

    private void CancelPendingPreview()
    {
        var job = _pendingPreview;
        if (job is null)
            return;
        _pendingPreview = null;
        job.Cancellation.Cancel();
        job.Cancellation.Dispose();
        if (_linesById.TryGetValue(job.LineId, out var line)
            && (_activeTranslation?.LineId != line.Id)
            && !_finalJobs.Any(candidate => candidate.LineId == line.Id))
        {
            line.IsTranslating = false;
            PublishLine(line);
        }
    }

    private void CancelAndDetachActiveTranslation(bool preserveReadableDisplay = false)
    {
        var job = _activeTranslation;
        if (job is null || job.IsObsolete)
            return;
        job.IsObsolete = true;
        TryCancel(job.Cancellation);
        if (_linesById.TryGetValue(job.LineId, out var line))
        {
            var rolledBack = !preserveReadableDisplay
                             && (job.IsStructured || job.StructuredPlanBuilder is not null);
            if (rolledBack)
                RollbackStructuredTranslation(line, job);
            if (!_finalJobs.Any(candidate => candidate.LineId == line.Id))
            {
                line.IsTranslating = false;
                PublishLine(line);
            }
            else if (rolledBack)
            {
                PublishLine(line);
            }
        }
    }

    private static void TryCancel(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void CancelPendingJobs()
    {
        CancelPendingPreview();
        foreach (var job in _finalJobs)
        {
            job.Cancellation.Cancel();
            job.Cancellation.Dispose();
        }
        _finalJobs.Clear();
    }

    private void CancelDisabledTranslationWork(TimeSpan now)
    {
        var affectedLineIds = new HashSet<long>();
        if (_pendingPreview is { } pending)
            affectedLineIds.Add(pending.LineId);
        if (_activeTranslation is { IsObsolete: false } active)
            affectedLineIds.Add(active.LineId);
        foreach (var job in _finalJobs)
            affectedLineIds.Add(job.LineId);
        if (affectedLineIds.Count == 0)
            return;

        CancelAndDetachActiveTranslation();
        CancelPendingJobs();
        foreach (var lineId in affectedLineIds)
        {
            if (!_linesById.TryGetValue(lineId, out var line))
                continue;
            line.TranslationDefinition = null;
            line.ShadowTranslationDefinition = null;
            line.LastTranslationDefinition = null;
            line.LastPreviewRequestedSource = string.Empty;
            line.PreviewEligibleAt = !line.IsSealed && IsPreviewEligible(line.OriginalText)
                ? now
                : null;
            if (line.IsSealed)
            {
                if (!line.IsTranslationTerminal || line.IsTranslating)
                    MarkTranslationTerminal(line, now);
            }
            else if (line.IsTranslating)
            {
                line.IsTranslating = false;
                PublishLine(line);
            }
        }
    }

    private async Task TryWriteCompletionAsync(
        TranslationCompletedMessage message,
        CancellationToken sessionToken)
    {
        try
        {
            await _inbox.Writer.WriteAsync(message, sessionToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (sessionToken.IsCancellationRequested)
        {
        }
    }

    private static bool IsPreviewEligible(string text) =>
        IncrementalSubtitleSegmenter.CountWords(text) >= 4
        || IncrementalSubtitleSegmenter.CountGraphemes(text) >= 8;

    private static bool IsReadableTranslation(string text) =>
        IncrementalSubtitleSegmenter.CountWords(text) >= 2
        || IncrementalSubtitleSegmenter.CountGraphemes(text) >= 6;

    private SubtitleTranslationLane GetTranslationLane(
        TranslationProviderSelection selection) =>
        string.Equals(
            selection.Engine,
            TranslationEngineNames.MachineTrans,
            StringComparison.Ordinal)
            ? _machineTranslationLane
            : _aiTranslationLane;

    private static bool UsesBufferedAiTranslation(SpeechRecognitionSettings settings) =>
        settings.IsTranslationEnabled && settings.EngineType != 0;

    private static bool IsBufferedAiTranslationFull(ManagedSubtitleLine line) =>
        IncrementalSubtitleSegmenter.CountWords(line.OriginalText)
            >= AiMaximumWordsPerTranslation
        || IncrementalSubtitleSegmenter.CountDisplayColumns(line.OriginalText)
            >= AiMaximumDisplayColumnsPerTranslation;

    private static string JoinText(string left, string right)
    {
        left = left.TrimEnd();
        right = right.TrimStart();
        if (left.Length == 0)
            return right;
        if (right.Length == 0)
            return left;
        if (IsPunctuation(right[0]) || IsCjk(left[^1]) || IsCjk(right[0]))
            return left + right;
        return left + " " + right;
    }

    private static bool IsPunctuation(char character) =>
        char.IsPunctuation(character) && character is not '(' and not '[' and not '{';

    private static bool IsCjk(char character) =>
        character is >= '\u2e80' and <= '\u9fff'
            or >= '\uac00' and <= '\ud7af'
            or >= '\uf900' and <= '\ufaff';

    private static string MapRecognitionLanguage(string modelName)
    {
        if (modelName.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return "zh-Hans";
        if (modelName.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return "en";
        if (modelName.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            return "ja";
        if (modelName.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
            return "ko";
        return "auto";
    }

    private TimeSpan GetMonotonicNow() =>
        _floatingLifecycle.GetMonotonicNow();

    private static async Task IgnoreCancellationAsync(Task task, CancellationToken cancellationToken)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private abstract record SessionMessage;
    private sealed record RecognitionMessage(SpeechRecognitionEvent Event) : SessionMessage;
    private sealed record RecognitionFailureMessage(Exception Exception) : SessionMessage;
    private sealed record TickMessage(TimeSpan Now) : SessionMessage;
    private sealed record TranslationBufferMessage(
        long JobId,
        long LineId,
        long Revision,
        string SourceText,
        string Text) : SessionMessage;
    private sealed record StructuredTranslationStartedMessage(
        long JobId,
        long LineId,
        long Revision,
        string SourceText) : SessionMessage;
    private sealed record StructuredTranslationSegmentMessage(
        long JobId,
        long LineId,
        long Revision,
        string SourceText,
        JsonElement Item) : SessionMessage;
    private sealed record TranslationCompletedMessage(
        long JobId,
        long LineId,
        long Revision,
        string SourceText,
        string Text,
        Exception? Exception,
        bool WasCanceled,
        bool WasStructured) : SessionMessage;

    private sealed record TranslationRunResult(
        string Text,
        Exception? Exception,
        bool WasCanceled,
        bool WasStructured);

    private sealed record TranslationJobDefinition(
        string RequestText,
        TranslationProviderSelection Selection,
        TranslationLanguage SourceLanguage,
        TranslationLanguage TargetLanguage);

    private sealed record TranslationDisplaySnapshot(
        string SourceText,
        string TranslatedText,
        string DisplayTranslatedText,
        TranslationJobDefinition? Definition,
        bool IsTranslationTerminal,
        TimeSpan? ExpiresAt);

    private sealed class TranslationJob(
        long id,
        long lineId,
        long revision,
        string sourceText,
        bool isFinal,
        TranslationJobDefinition definition,
        CancellationTokenSource cancellation,
        TranslationDisplaySnapshot? recoveryDisplay,
        bool preserveDisplayUntilCompleted,
        int attempt,
        TimeSpan? notBefore)
    {
        public long Id { get; } = id;
        public long LineId { get; } = lineId;
        public long Revision { get; } = revision;
        public string SourceText { get; } = sourceText;
        public TranslationJobDefinition Definition { get; } = definition;
        public string RequestText => Definition.RequestText;
        public bool IsFinal { get; set; } = isFinal;
        public TranslationProviderSelection Selection => Definition.Selection;
        public TranslationLanguage SourceLanguage => Definition.SourceLanguage;
        public TranslationLanguage TargetLanguage => Definition.TargetLanguage;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public TranslationDisplaySnapshot? RecoveryDisplay { get; } = recoveryDisplay;
        public bool PreserveDisplayUntilCompleted { get; } = preserveDisplayUntilCompleted;
        public int Attempt { get; } = attempt;
        public TimeSpan? NotBefore { get; } = notBefore;
        public object ProviderRunKey { get; } = new();
        public CancellationTokenRegistration SessionRegistration { get; set; }
        public Task? Runner { get; set; }
        public string Buffer { get; set; } = string.Empty;
        public JsonLinesSubtitlePlanBuilder? StructuredPlanBuilder { get; set; }
        public bool IsStructured { get; set; }
        public bool Revealed { get; set; }
        public bool IsObsolete { get; set; }
        public TimeSpan NextDisplayAt { get; set; }
    }

    private sealed class ManagedSubtitleLine(long id, TimeSpan timestamp, TimeSpan createdAt)
    {
        public long Id { get; } = id;
        public TimeSpan Timestamp { get; } = timestamp;
        public long Revision { get; set; }
        public string OriginalText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
        public string DisplayTranslatedText { get; set; } = string.Empty;
        public string ShadowTranslation { get; set; } = string.Empty;
        public string ShadowTranslationSource { get; set; } = string.Empty;
        public TranslationJobDefinition? ShadowTranslationDefinition { get; set; }
        public string LastTranslatedSource { get; set; } = string.Empty;
        public TranslationJobDefinition? LastTranslationDefinition { get; set; }
        public TranslationJobDefinition? TranslationDefinition { get; set; }
        public JsonLinesSubtitlePlan? StructuredPlan { get; set; }
        public string StructuredPlanSource { get; set; } = string.Empty;
        public TranslationJobDefinition? StructuredPlanDefinition { get; set; }
        public string LastPreviewRequestedSource { get; set; } = string.Empty;
        public bool IsTranslating { get; set; }
        public bool IsTranslationTerminal { get; set; }
        public bool IsTemporary { get; set; } = true;
        public bool IsSealed { get; set; }
        public bool IsFloatingVisible { get; set; } = true;
        public TimeSpan LastSourceChangedAt { get; set; } = createdAt;
        public TimeSpan? PreviewEligibleAt { get; set; }
        public TimeSpan? ExpiresAt { get; set; }
        public int SourceStart { get; set; }
        public int SourceEnd { get; set; }
        public bool IsSourceFinalized { get; set; }
        public List<long> ReservedChildIds { get; } = [];

        public SpeechSubtitleLine Snapshot() => new(
            Id,
            Timestamp,
            OriginalText,
            TranslatedText,
            DisplayTranslatedText,
            IsTranslating,
            IsTemporary);
    }

    private sealed class UtteranceLineRange(
        ManagedSubtitleLine line,
        int start,
        int end)
    {
        public ManagedSubtitleLine Line { get; } = line;
        public int Start { get; set; } = start;
        public int End { get; set; } = end;
    }

    private readonly record struct SubtitleSourceRange(int Start, int End);

    private sealed record SubtitleTranslationContext(string Original, string Translation);
}
