using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace EasyChat.Application.Speech;

internal sealed record JsonLinesSubtitleSegment(
    int Sequence,
    string Source,
    string Translation,
    bool IsFinal);

internal sealed record JsonLinesSubtitlePlan(
    ImmutableArray<JsonLinesSubtitleSegment> Segments);

internal readonly record struct JsonLinesSubtitlePlanPrefix(
    string Translation,
    int CoveredLength,
    int Count);

internal sealed class JsonLinesSubtitlePlanBuilder
{
    private readonly string _sourceSnapshot;
    private readonly List<JsonLinesSubtitleSegment> _segments = [];
    private readonly StringBuilder _aggregateTranslation = new();
    private JsonLinesSubtitlePlan? _completedPlan;
    private int _coveredSourceLength;
    private bool _cannotAcceptMoreRecords;
    private bool _failed;

    public JsonLinesSubtitlePlanBuilder(string sourceSnapshot)
    {
        _sourceSnapshot = sourceSnapshot
                          ?? throw new ArgumentNullException(nameof(sourceSnapshot));
    }

    public bool TryAdd(JsonElement item, out JsonLinesSubtitlePlanPrefix prefix)
    {
        prefix = default;
        if (_failed || _completedPlan is not null || _cannotAcceptMoreRecords)
        {
            _failed = _completedPlan is null;
            return false;
        }

        if (!TryReadSegment(item, out var segment)
            || segment.Sequence != _segments.Count
            || segment.Source.Length == 0
            || segment.Translation.AsSpan().Trim().Length == 0
            || !TryMatchSourceSlice(segment.Source, out var matchedSource))
        {
            _failed = true;
            return false;
        }

        segment = segment with { Source = matchedSource };
        if (!HasConsistentSentenceFinality(segment))
        {
            _failed = true;
            return false;
        }

        _segments.Add(segment);
        _coveredSourceLength += segment.Source.Length;
        _aggregateTranslation.Append(segment.Translation);
        _cannotAcceptMoreRecords = !segment.IsFinal;
        prefix = new JsonLinesSubtitlePlanPrefix(
            _aggregateTranslation.ToString(),
            _coveredSourceLength,
            _segments.Count);
        return true;
    }

    public bool TryComplete([NotNullWhen(true)] out JsonLinesSubtitlePlan? plan)
    {
        if (_completedPlan is not null)
        {
            plan = _completedPlan;
            return true;
        }

        if (_failed
            || _segments.Count == 0
            || _coveredSourceLength != _sourceSnapshot.Length)
        {
            _failed = true;
            plan = null;
            return false;
        }

        _completedPlan = new JsonLinesSubtitlePlan(_segments.ToImmutableArray());
        plan = _completedPlan;
        return true;
    }

    private static bool TryReadSegment(
        JsonElement item,
        [NotNullWhen(true)] out JsonLinesSubtitleSegment? segment)
    {
        segment = null;
        if (!HasExactSchema(item)
            || !item.TryGetProperty("seq", out var sequenceElement)
            || sequenceElement.ValueKind != JsonValueKind.Number
            || !sequenceElement.TryGetInt32(out var sequence)
            || !item.TryGetProperty("source", out var sourceElement)
            || sourceElement.ValueKind != JsonValueKind.String
            || !item.TryGetProperty("translation", out var translationElement)
            || translationElement.ValueKind != JsonValueKind.String
            || !item.TryGetProperty("final", out var finalElement)
            || finalElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        var source = sourceElement.GetString();
        var translation = translationElement.GetString();
        if (source is null || translation is null)
            return false;

        segment = new JsonLinesSubtitleSegment(
            sequence,
            source,
            translation,
            finalElement.GetBoolean());
        return true;
    }

    private static bool HasExactSchema(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return false;

        const int Sequence = 1 << 0;
        const int Source = 1 << 1;
        const int Translation = 1 << 2;
        const int Final = 1 << 3;
        const int CompleteSchema = Sequence | Source | Translation | Final;
        var fields = 0;
        foreach (var property in item.EnumerateObject())
        {
            var field = property.Name switch
            {
                "seq" => Sequence,
                "source" => Source,
                "translation" => Translation,
                "final" => Final,
                _ => 0
            };
            if (field == 0 || (fields & field) != 0)
                return false;
            fields |= field;
        }

        return fields == CompleteSchema;
    }

    private bool TryMatchSourceSlice(string emittedSource, out string matchedSource)
    {
        matchedSource = string.Empty;
        var remaining = _sourceSnapshot.AsSpan(_coveredSourceLength);
        if (remaining.StartsWith(emittedSource.AsSpan(), StringComparison.Ordinal))
        {
            matchedSource = emittedSource;
            return true;
        }

        var sourceLeadingWhitespace = 0;
        while (sourceLeadingWhitespace < remaining.Length
               && char.IsWhiteSpace(remaining[sourceLeadingWhitespace]))
        {
            sourceLeadingWhitespace++;
        }

        var trimmedEmitted = emittedSource.AsSpan().TrimStart();
        if (trimmedEmitted.Length == 0
            || !remaining[sourceLeadingWhitespace..].StartsWith(
                trimmedEmitted,
                StringComparison.Ordinal))
        {
            return false;
        }

        var matchedLength = sourceLeadingWhitespace + trimmedEmitted.Length;
        matchedSource = _sourceSnapshot.Substring(_coveredSourceLength, matchedLength);
        return true;
    }

    private static bool HasConsistentSentenceFinality(JsonLinesSubtitleSegment segment)
    {
        var boundaries = IncrementalSubtitleSegmenter.FindStrongBoundaries(segment.Source);
        if (boundaries.Count == 0)
            return true;
        if (!segment.IsFinal)
            return false;
        return segment.Source.AsSpan(boundaries[^1]).Trim().IsEmpty;
    }
}
