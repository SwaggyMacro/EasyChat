using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EasyChat.Application.Speech;

internal sealed record SubtitleSegmentCommit(
    string Text,
    int SentenceCount,
    bool CloseLine,
    int SourceStart = -1,
    int SourceEnd = -1);

internal sealed record SubtitleSegmentationUpdate(
    IReadOnlyList<SubtitleSegmentCommit> Commits,
    string DraftText,
    bool CloseCurrentLine = false,
    string? AppendToPreviousLine = null,
    bool EndsUtterance = false,
    string Hypothesis = "",
    int DraftStart = 0,
    bool ReconcileFinal = false,
    string PreviousHypothesis = "",
    bool StartsNewUtterance = false)
{
    public static readonly SubtitleSegmentationUpdate Empty = new([], string.Empty);
}

internal sealed class IncrementalSubtitleSegmenter
{
    internal static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(1200);
    internal static readonly TimeSpan TargetSegmentDuration = TimeSpan.FromMilliseconds(2500);
    internal static readonly TimeSpan HardSegmentDuration = TimeSpan.FromSeconds(4);
    internal const int MaximumWords = 16;
    internal const int MaximumDisplayColumns = 48;
    private const int MinimumForcedColumns = 18;
    private const int RevisionTailWords = 2;
    private const int RevisionTailGraphemes = 4;

    private static readonly Regex RepeatedHorizontalWhitespace = new(
        @"[\t\f\v ]+",
        RegexOptions.Compiled);
    private static readonly Regex SurroundingNewlineWhitespace = new(
        @" *\n *",
        RegexOptions.Compiled);
    private static readonly Regex RepeatedNewlines = new(
        @"\n{2,}",
        RegexOptions.Compiled);
    private static readonly Regex WordPattern = new(
        @"[\p{L}\p{M}\p{N}]+(?:['\-][\p{L}\p{M}\p{N}]+)*",
        RegexOptions.Compiled);
    private static readonly HashSet<string> CommonAbbreviations = new(
        [
            "mr", "mrs", "ms", "dr", "prof", "sr", "jr", "st", "vs", "etc",
            "e.g", "i.e", "u.s", "u.k", "a.m", "p.m", "no", "fig", "inc", "ltd", "corp"
        ],
        StringComparer.OrdinalIgnoreCase);

    private string _previousHypothesis = string.Empty;
    private string _latestHypothesis = string.Empty;
    private string _consumedPrefix = string.Empty;
    private int _stableLength;
    private TimeSpan _segmentStartedAt;
    private TimeSpan _lastChangedAt;
    private bool _hasHypothesis;
    private bool _quietHandled;

    internal string LatestHypothesis => _latestHypothesis;

    public SubtitleSegmentationUpdate ApplyPartial(string text, TimeSpan now)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0)
            return SubtitleSegmentationUpdate.Empty;

        var startsNewUtterance = _quietHandled
                                 && _consumedPrefix.Length > 0
                                 && !normalized.StartsWith(
                                     _consumedPrefix,
                                     StringComparison.OrdinalIgnoreCase);
        if (startsNewUtterance)
            Reset();

        var changed = !string.Equals(normalized, _latestHypothesis, StringComparison.Ordinal);
        if (!_hasHypothesis)
        {
            _hasHypothesis = true;
            _segmentStartedAt = now;
            _lastChangedAt = now;
        }
        else if (changed)
        {
            _lastChangedAt = now;
        }

        _quietHandled &= !changed;
        _stableLength = _previousHypothesis.Length == 0
            ? 0
            : AlignToTextElementBoundary(
                normalized,
                LongestCommonPrefix(_previousHypothesis, normalized));
        _latestHypothesis = normalized;
        ReconcileConsumedPrefix(normalized);

        var commits = new List<SubtitleSegmentCommit>();
        CommitStrongSentences(commits, now);
        CommitForcedSegments(commits, now);
        _previousHypothesis = normalized;

        return new SubtitleSegmentationUpdate(
            commits,
            RemainingText(),
            Hypothesis: _latestHypothesis,
            DraftStart: DraftSourceStart(),
            StartsNewUtterance: startsNewUtterance);
    }

    public SubtitleSegmentationUpdate Tick(TimeSpan now, TimeSpan? quietPeriod = null)
    {
        var requiredQuietPeriod = quietPeriod ?? QuietPeriod;
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredQuietPeriod, TimeSpan.Zero);
        if (!_hasHypothesis || _quietHandled || now - _lastChangedAt < requiredQuietPeriod)
            return SubtitleSegmentationUpdate.Empty;

        _quietHandled = true;
        var draftStart = DraftSourceStart();
        var remaining = RemainingText();
        if (remaining.Length == 0)
        {
            return new SubtitleSegmentationUpdate(
                [],
                string.Empty,
                CloseCurrentLine: true,
                Hypothesis: _latestHypothesis,
                DraftStart: draftStart);
        }

        ConsumeTo(_latestHypothesis.Length);
        return new SubtitleSegmentationUpdate(
            [new SubtitleSegmentCommit(
                remaining,
                Math.Max(1, CountSentences(remaining)),
                true,
                draftStart,
                _latestHypothesis.Length)],
            string.Empty,
            CloseCurrentLine: true,
            Hypothesis: _latestHypothesis,
            DraftStart: _latestHypothesis.Length);
    }

    public SubtitleSegmentationUpdate ApplyFinal(string text, TimeSpan now)
    {
        var previousHypothesis = _latestHypothesis;
        var normalized = Normalize(text);
        if (normalized.Length == 0)
            return CompleteLatest();

        _hasHypothesis = true;
        _latestHypothesis = normalized;
        _stableLength = normalized.Length;
        ReconcileConsumedPrefix(normalized);
        var draftStart = DraftSourceStart();
        var remaining = RemainingText();

        if (remaining.Length > 0 && IsOnlyTerminalSuffix(remaining) && _consumedPrefix.Length > 0)
        {
            var punctuation = new SubtitleSegmentationUpdate(
                [],
                string.Empty,
                CloseCurrentLine: true,
                AppendToPreviousLine: remaining,
                EndsUtterance: true,
                Hypothesis: normalized,
                DraftStart: draftStart,
                ReconcileFinal: true,
                PreviousHypothesis: previousHypothesis);
            Reset();
            return punctuation;
        }

        var update = new SubtitleSegmentationUpdate(
            SplitFinalText(remaining, draftStart),
            string.Empty,
            CloseCurrentLine: true,
            EndsUtterance: true,
            Hypothesis: normalized,
            DraftStart: normalized.Length,
            ReconcileFinal: true,
            PreviousHypothesis: previousHypothesis);
        Reset();
        return update;
    }

    public SubtitleSegmentationUpdate CompleteLatest()
    {
        if (!_hasHypothesis)
            return new SubtitleSegmentationUpdate([], string.Empty, EndsUtterance: true);

        var hypothesis = _latestHypothesis;
        var previousHypothesis = _previousHypothesis;
        var draftStart = DraftSourceStart();
        var remaining = RemainingText();
        var commits = remaining.Length == 0
            ? Array.Empty<SubtitleSegmentCommit>()
            : [new SubtitleSegmentCommit(
                remaining,
                Math.Max(1, CountSentences(remaining)),
                true,
                draftStart,
                hypothesis.Length)];
        var update = new SubtitleSegmentationUpdate(
            commits,
            string.Empty,
            CloseCurrentLine: true,
            EndsUtterance: true,
            Hypothesis: hypothesis,
            DraftStart: hypothesis.Length,
            ReconcileFinal: true,
            PreviousHypothesis: previousHypothesis);
        Reset();
        return update;
    }

    public void Reset()
    {
        _previousHypothesis = string.Empty;
        _latestHypothesis = string.Empty;
        _consumedPrefix = string.Empty;
        _stableLength = 0;
        _segmentStartedAt = default;
        _lastChangedAt = default;
        _hasHypothesis = false;
        _quietHandled = false;
    }

    internal static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var value = text.Normalize(NormalizationForm.FormC)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        value = RepeatedHorizontalWhitespace.Replace(value, " ");
        value = SurroundingNewlineWhitespace.Replace(value, "\n");
        value = RepeatedNewlines.Replace(value, "\n");
        return value.Trim();
    }

    internal static int CountWords(string text) => WordPattern.Matches(text).Count;

    internal static int CountGraphemes(string text) =>
        string.IsNullOrEmpty(text) ? 0 : StringInfo.ParseCombiningCharacters(text).Length;

    internal static int CountDisplayColumns(string text)
    {
        var columns = 0;
        foreach (var position in StringInfo.ParseCombiningCharacters(text))
        {
            var rune = Rune.GetRuneAt(text, position);
            columns += IsWide(rune.Value) ? 2 : 1;
        }
        return columns;
    }

    private void CommitStrongSentences(List<SubtitleSegmentCommit> commits, TimeSpan now)
    {
        var consumedLength = _consumedPrefix.Length;
        foreach (var boundary in FindStrongBoundaries(_latestHypothesis))
        {
            if (boundary <= consumedLength || boundary > _stableLength)
                continue;
            var sourceStart = SkipWhitespace(_latestHypothesis, consumedLength, boundary);
            var sourceEnd = TrimTrailingWhitespace(_latestHypothesis, sourceStart, boundary);
            var text = _latestHypothesis[sourceStart..sourceEnd];
            ConsumeTo(boundary);
            consumedLength = _consumedPrefix.Length;
            if (text.Length == 0)
                continue;
            commits.Add(new SubtitleSegmentCommit(
                text,
                Math.Max(1, CountSentences(text)),
                false,
                sourceStart,
                sourceEnd));
            _segmentStartedAt = now;
        }
    }

    private void CommitForcedSegments(List<SubtitleSegmentCommit> commits, TimeSpan now)
    {
        while (true)
        {
            var remaining = RemainingText();
            if (remaining.Length == 0)
                return;

            var age = now - _segmentStartedAt;
            var sizeTriggered = CountWords(remaining) >= MaximumWords
                                || CountDisplayColumns(remaining) >= MaximumDisplayColumns;
            var timeTriggered = age >= TargetSegmentDuration;
            var hardTriggered = age >= HardSegmentDuration;
            if (!sizeTriggered && !timeTriggered && !hardTriggered)
                return;

            var stableRemaining = Math.Max(0, _stableLength - _consumedPrefix.Length);
            var candidateLength = hardTriggered
                ? remaining.Length
                : Math.Min(remaining.Length, stableRemaining);
            if (!hardTriggered)
                candidateLength = RemoveRevisionTail(remaining, candidateLength);
            if (candidateLength <= 0)
                return;

            var candidate = remaining[..candidateLength].TrimEnd();
            if (!hardTriggered && CountDisplayColumns(candidate) < MinimumForcedColumns)
                return;
            var cut = FindForcedCut(
                candidate,
                timeTriggered || hardTriggered,
                MaximumWords,
                MaximumDisplayColumns);
            if (cut <= 0)
                return;

            var committed = candidate[..cut].Trim();
            if (committed.Length == 0)
                return;
            var sourceStart = DraftSourceStart();
            var absoluteCut = _consumedPrefix.Length + FindConsumedLength(remaining, committed.Length);
            ConsumeTo(Math.Min(absoluteCut, _latestHypothesis.Length));
            commits.Add(new SubtitleSegmentCommit(
                committed,
                Math.Max(1, CountSentences(committed)),
                true,
                sourceStart,
                sourceStart + committed.Length));
            _segmentStartedAt = now;
        }
    }

    private static IReadOnlyList<SubtitleSegmentCommit> SplitFinalText(
        string remaining,
        int sourceOffset)
    {
        if (remaining.Length == 0)
            return [];
        var result = new List<SubtitleSegmentCommit>();
        var start = 0;
        foreach (var boundary in FindStrongBoundaries(remaining))
        {
            var sentence = remaining[start..boundary].Trim();
            if (sentence.Length > 0)
            {
                var leading = remaining[start..boundary].Length
                              - remaining[start..boundary].TrimStart().Length;
                result.Add(new SubtitleSegmentCommit(
                    sentence,
                    1,
                    false,
                    sourceOffset + start + leading,
                    sourceOffset + start + leading + sentence.Length));
            }
            start = boundary;
        }
        var tail = remaining[start..].Trim();
        if (tail.Length > 0)
        {
            var leading = remaining[start..].Length - remaining[start..].TrimStart().Length;
            result.Add(new SubtitleSegmentCommit(
                tail,
                Math.Max(1, CountSentences(tail)),
                false,
                sourceOffset + start + leading,
                sourceOffset + start + leading + tail.Length));
        }
        return result;
    }

    private string RemainingText()
    {
        var offset = Math.Min(_consumedPrefix.Length, _latestHypothesis.Length);
        return _latestHypothesis[offset..].TrimStart();
    }

    private int DraftSourceStart() =>
        SkipWhitespace(_latestHypothesis, _consumedPrefix.Length, _latestHypothesis.Length);

    private void ConsumeTo(int length)
    {
        length = Math.Clamp(length, 0, _latestHypothesis.Length);
        _consumedPrefix = _latestHypothesis[..length];
        while (_consumedPrefix.Length < _latestHypothesis.Length
               && char.IsWhiteSpace(_latestHypothesis[_consumedPrefix.Length]))
        {
            _consumedPrefix = _latestHypothesis[..(_consumedPrefix.Length + 1)];
        }
    }

    private void ReconcileConsumedPrefix(string hypothesis)
    {
        if (_consumedPrefix.Length == 0
            || hypothesis.StartsWith(_consumedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Published captions cannot be retracted through the current public event contract.
        // Preserve their positional span so a late ASR revision cannot duplicate the utterance.
        var consumedLength = Math.Min(_consumedPrefix.Length, hypothesis.Length);
        _consumedPrefix = hypothesis[..consumedLength];
        _stableLength = Math.Max(_stableLength, consumedLength);
    }

    private static int RemoveRevisionTail(string remaining, int availableLength)
    {
        if (availableLength <= 0)
            return 0;
        var candidate = remaining[..availableLength];
        if (ContainsWideText(candidate))
        {
            var positions = StringInfo.ParseCombiningCharacters(candidate);
            return positions.Length <= RevisionTailGraphemes
                ? 0
                : positions[^RevisionTailGraphemes];
        }

        var words = WordPattern.Matches(candidate);
        if (words.Count <= RevisionTailWords)
            return 0;
        return words[^RevisionTailWords].Index;
    }

    private static int FindForcedCut(
        string candidate,
        bool allowWholeCandidate,
        int maximumWords,
        int maximumDisplayColumns)
    {
        if (candidate.Length == 0)
            return 0;
        var columns = 0;
        var maximumIndex = candidate.Length;
        var positions = StringInfo.ParseCombiningCharacters(candidate);
        for (var elementIndex = 0; elementIndex < positions.Length; elementIndex++)
        {
            var position = positions[elementIndex];
            var rune = Rune.GetRuneAt(candidate, position);
            var next = elementIndex + 1 < positions.Length
                ? positions[elementIndex + 1]
                : candidate.Length;
            columns += IsWide(rune.Value) ? 2 : 1;
            if (columns > maximumDisplayColumns)
            {
                maximumIndex = position;
                break;
            }
            maximumIndex = next;
        }

        var words = WordPattern.Matches(candidate);
        if (words.Count > maximumWords)
            maximumIndex = Math.Min(maximumIndex, words[maximumWords].Index);
        if (maximumIndex <= 0)
            return 0;

        var minimumIndex = IndexAtDisplayColumn(candidate, MinimumForcedColumns);
        for (var index = Math.Min(maximumIndex, candidate.Length) - 1; index >= minimumIndex; index--)
        {
            if (IsWeakBoundary(candidate[index]))
                return index + 1;
        }
        for (var index = Math.Min(maximumIndex, candidate.Length) - 1; index >= minimumIndex; index--)
        {
            if (char.IsWhiteSpace(candidate[index]))
                return index;
        }
        return allowWholeCandidate || maximumIndex < candidate.Length ? maximumIndex : 0;
    }

    internal static int FindPreferredCut(
        string candidate,
        int maximumWords = MaximumWords,
        int maximumDisplayColumns = MaximumDisplayColumns) =>
        FindForcedCut(
            candidate,
            allowWholeCandidate: true,
            maximumWords,
            maximumDisplayColumns);

    private static int IndexAtDisplayColumn(string text, int target)
    {
        var columns = 0;
        foreach (var position in StringInfo.ParseCombiningCharacters(text))
        {
            var rune = Rune.GetRuneAt(text, position);
            columns += IsWide(rune.Value) ? 2 : 1;
            if (columns >= target)
                return position;
        }
        return 0;
    }

    private static int FindConsumedLength(string source, int trimmedLength)
    {
        var leading = 0;
        while (leading < source.Length && char.IsWhiteSpace(source[leading]))
            leading++;
        return Math.Min(source.Length, leading + trimmedLength);
    }

    internal static IReadOnlyList<int> FindStrongBoundaries(string text)
    {
        var result = new List<int>();
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '\n')
            {
                result.Add(index + 1);
                continue;
            }
            if (!IsTerminalMark(character))
                continue;
            if (character == '.')
            {
                if (index > 0 && index + 1 < text.Length
                              && char.IsDigit(text[index - 1]) && char.IsDigit(text[index + 1]))
                {
                    continue;
                }
                if (index + 1 < text.Length && text[index + 1] == '.')
                    continue;
                if (index + 1 < text.Length
                    && !char.IsWhiteSpace(text[index + 1])
                    && !IsClosingPunctuation(text[index + 1]))
                {
                    continue;
                }
                var tokenStart = index;
                while (tokenStart > 0 && !char.IsWhiteSpace(text[tokenStart - 1]))
                    tokenStart--;
                var token = text[tokenStart..index].Trim('(', '[', '{', '"', '\'');
                if (CommonAbbreviations.Contains(token))
                    continue;
            }

            var boundary = index + 1;
            while (boundary < text.Length && IsTerminalMark(text[boundary]))
                boundary++;
            while (boundary < text.Length && IsClosingPunctuation(text[boundary]))
                boundary++;
            result.Add(boundary);
            index = boundary - 1;
        }
        return result;
    }

    private static bool IsTerminalMark(char character) =>
        character is '.' or '!' or '?' or '\u3002' or '\uff01' or '\uff1f';

    private static bool IsClosingPunctuation(char character) =>
        character is ')' or ']' or '}' or '"' or '\''
            or '\u2019' or '\u201d' or '\u3009' or '\u300b'
            or '\u3011' or '\u300d' or '\u300f';

    private static int CountSentences(string text) => FindStrongBoundaries(text).Count;

    internal static bool IsOnlyTerminalPunctuation(string text) =>
        text.All(character => char.IsWhiteSpace(character)
                              || character is '.' or '!' or '?' or '\u3002' or '\uff01' or '\uff1f');

    internal static bool IsOnlyTerminalSuffix(string text) =>
        text.Length > 0 && text.All(character =>
            char.IsWhiteSpace(character)
            || character is '.' or '!' or '?' or '\u3002' or '\uff01' or '\uff1f'
                or ')' or ']' or '}' or '"' or '\''
                or '\u2019' or '\u201d' or '\u3009' or '\u300b'
                or '\u3011' or '\u300d' or '\u300f');

    private static bool IsWeakBoundary(char character) =>
        character is ',' or ';' or ':' or '\uff0c' or '\uff1b' or '\uff1a' or '\u3001';

    internal static int LongestCommonPrefix(string left, string right)
    {
        var length = Math.Min(left.Length, right.Length);
        var index = 0;
        while (index < length
               && char.ToUpperInvariant(left[index]) == char.ToUpperInvariant(right[index]))
        {
            index++;
        }
        return index;
    }

    internal static int AlignPrefixToTextElement(string text, int index) =>
        AlignToTextElementBoundary(text, index);

    internal static int SkipWhitespace(string text, int start, int end)
    {
        var index = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, index, text.Length);
        while (index < end && char.IsWhiteSpace(text[index]))
            index++;
        return index;
    }

    internal static int TrimTrailingWhitespace(string text, int start, int end)
    {
        start = Math.Clamp(start, 0, text.Length);
        var index = Math.Clamp(end, start, text.Length);
        while (index > start && char.IsWhiteSpace(text[index - 1]))
            index--;
        return index;
    }

    private static int AlignToTextElementBoundary(string text, int index)
    {
        if (index <= 0 || index >= text.Length)
            return Math.Clamp(index, 0, text.Length);
        var aligned = 0;
        foreach (var position in StringInfo.ParseCombiningCharacters(text))
        {
            if (position > index)
                break;
            aligned = position;
        }
        return aligned;
    }

    private static bool ContainsWideText(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (IsWide(rune.Value))
                return true;
        }
        return false;
    }

    private static bool IsWide(int value) =>
        value is >= 0x1100 and <= 0x11ff
            or >= 0x2e80 and <= 0xa4cf
            or >= 0xac00 and <= 0xd7af
            or >= 0xf900 and <= 0xfaff
            or >= 0xfe10 and <= 0xfe6f
            or >= 0xff01 and <= 0xff60
            or >= 0x1f300 and <= 0x1faff;
}
