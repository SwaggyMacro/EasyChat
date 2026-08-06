using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using EasyChat.Contracts.Translation;
using EasyChat.Shared.Streaming;

namespace EasyChat.Application.Translation;

internal interface IStructuredJsonLinesTranslationSession
{
    IAsyncEnumerable<JsonElement> StreamJsonLinesAsync(
        TranslationRequest request,
        string runtimeContract,
        CancellationToken cancellationToken = default);
}

internal sealed class MachineTranslationSession : ITranslationSession, IDisposable
{
    private readonly ITranslationProvider _provider;
    private readonly string _providerName;

    public MachineTranslationSession(ITranslationProvider provider, string providerName)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _providerName = providerName;
    }

    public bool SupportsIdentifiedStreaming => false;

    public async Task<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = request.Source
                     ?? throw new ArgumentNullException(nameof(request.Source));
        var text = await _provider.TranslateAsync(
            new TranslationProviderRequest(
                request.Text,
                ResolveLanguageCode(source),
                ResolveLanguageCode(request.Target),
                request.ShowOriginal),
            cancellationToken).ConfigureAwait(false);
        return new TranslationResponse(text);
    }

    public async IAsyncEnumerable<TranslationEvent> StreamAsync(
        TranslationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = request.Source
                     ?? throw new ArgumentNullException(nameof(request.Source));
        yield return new TranslationStartedEvent(
            "translation",
            source.EnglishName,
            request.Target.EnglishName);
        var response = await TranslateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(response.Text))
            yield return new TranslationDeltaEvent(response.Text);
        yield return new TranslationCompletedEvent();
    }

    public IAsyncEnumerable<IdentifiedTranslationDelta> StreamIdentifiedAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "The configured machine translator does not support identified streams.");

    public void Dispose()
    {
        if (_provider is IDisposable disposable)
            disposable.Dispose();
    }

    private string ResolveLanguageCode(TranslationLanguage language)
    {
        if (language.ProviderCodes is not null
            && language.ProviderCodes.TryGetValue(_providerName, out var code)
            && !string.IsNullOrWhiteSpace(code))
        {
            return code;
        }

        return language.Id;
    }
}

internal sealed class AiTranslationSession :
    ITranslationSession,
    IStructuredJsonLinesTranslationSession,
    IDisposable
{
    private readonly IChatTranslationProvider _provider;
    private readonly string _promptTemplate;

    public AiTranslationSession(IChatTranslationProvider provider, string promptTemplate)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _promptTemplate = promptTemplate ?? throw new ArgumentNullException(nameof(promptTemplate));
    }

    public bool SupportsIdentifiedStreaming => true;

    public async Task<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateLanguages(request);
        var response = await _provider.CompleteAsync(
            new ChatTranslationProviderRequest(
                CreateStructuredPrompt(request.Source!, request.Target, request.PlainText),
                request.Text),
            cancellationToken).ConfigureAwait(false);
        return new TranslationResponse(ExtractTranslation(response));
    }

    public async IAsyncEnumerable<TranslationEvent> StreamAsync(
        TranslationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateLanguages(request);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var decoder = new JsonLinesDeltaStreamDecoder<TranslationEvent>(
            line => JsonSerializer.Deserialize<TranslationEvent>(line, options)
                    ?? throw new JsonException("Empty translation event."),
            "translation_delta",
            "text");

        await foreach (var chunk in _provider.StreamAsync(
                           new ChatTranslationProviderRequest(
                               CreateStructuredPrompt(request.Source!, request.Target, request.PlainText),
                               request.Text),
                           cancellationToken).ConfigureAwait(false))
        {
            foreach (var item in decoder.Append(chunk))
            {
                if (item is not TranslationCompletedEvent)
                    yield return item;
            }
        }

        foreach (var item in decoder.Complete())
        {
            if (item is not TranslationCompletedEvent)
                yield return item;
        }

        yield return new TranslationCompletedEvent();
    }

    public async IAsyncEnumerable<IdentifiedTranslationDelta> StreamIdentifiedAsync(
        TranslationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateLanguages(request);
        var decoder = new JsonLinesEventStreamDecoder<JsonElement>(
            line => JsonSerializer.Deserialize<JsonElement>(line));
        await foreach (var chunk in _provider.StreamAsync(
                           new ChatTranslationProviderRequest(
                               CreateIdentifiedPrompt(request.Source!, request.Target),
                               request.Text),
                           cancellationToken).ConfigureAwait(false))
        {
            foreach (var item in decoder.Append(chunk))
            {
                if (TryReadIdentifiedDelta(item, out var delta))
                    yield return delta;
            }
        }

        foreach (var item in decoder.Complete())
        {
            if (TryReadIdentifiedDelta(item, out var delta))
                yield return delta;
        }
    }

    public async IAsyncEnumerable<JsonElement> StreamJsonLinesAsync(
        TranslationRequest request,
        string runtimeContract,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateLanguages(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeContract);
        var decoder = new StrictJsonLinesElementStreamDecoder();
        await foreach (var chunk in _provider.StreamAsync(
                           new ChatTranslationProviderRequest(
                               CreateSuppliedContractPrompt(
                                   request.Source!,
                                   request.Target,
                                   runtimeContract),
                               request.Text),
                           cancellationToken).ConfigureAwait(false))
        {
            foreach (var item in decoder.Append(chunk))
                yield return item;
        }

        foreach (var item in decoder.Complete())
            yield return item;
    }

    public void Dispose()
    {
        if (_provider is IDisposable disposable)
            disposable.Dispose();
    }

    private string ExtractTranslation(string response)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var decoder = new JsonLinesDeltaStreamDecoder<TranslationEvent>(
            line => JsonSerializer.Deserialize<TranslationEvent>(line, options)
                    ?? throw new JsonException("Empty translation event."),
            "translation_delta",
            "text");
        var result = new StringBuilder();
        foreach (var item in decoder.Append(response))
        {
            if (item is TranslationDeltaEvent delta)
                result.Append(delta.Text);
        }

        foreach (var item in decoder.Complete())
        {
            if (item is TranslationDeltaEvent delta)
                result.Append(delta.Text);
        }

        return result.Length > 0
            ? result.ToString()
            : StripMarkdownFence(response.Trim());
    }

    private string CreateStructuredPrompt(
        TranslationLanguage source,
        TranslationLanguage target,
        bool plainText)
    {
        var prompt = ApplyLanguages(_promptTemplate, source, target);
        var contract = "\n\n# Runtime JSONL translation contract (highest priority)\n"
                       + "The contract below has higher priority than any earlier instruction. "
                       + "If an earlier instruction conflicts with it, ignore the conflicting part.\n"
                       + "Return raw NDJSON only: one complete JSON object per line, with no Markdown fences "
                       + "or explanatory text. Escape JSON strings correctly.\n"
                       + "Emit exactly this order:\n"
                       + "{\"event\":\"start\",\"mode\":\"translation\",\"source_language\":\"[SourceLang]\",\"target_language\":\"[TargetLang]\"}\n"
                       + "Optionally emit one {\"event\":\"source_detected\",\"language\":\"language id\"} event when source was auto-detected.\n"
                       + "Emit one or more {\"event\":\"translation_delta\",\"text\":\"...\"} events. "
                       + "Concatenating all text values must be the complete translation.\n"
                       + "Finish with exactly {\"event\":\"done\"}.\n";
        if (plainText)
        {
            contract += "The translated text must be plain text for direct input delivery. "
                        + "Do not use Markdown formatting, headings, list markers, or code fences.\n";
        }
        return ApplyLanguages(prompt + contract, source, target);
    }

    private string CreateIdentifiedPrompt(
        TranslationLanguage source,
        TranslationLanguage target)
    {
        var prompt = ApplyLanguages(_promptTemplate, source, target);
        var contract = "\n\n# Identified JSONL translation contract (highest priority)\n"
                       + "The contract below has higher priority than any earlier output-format instruction. "
                       + "Return raw NDJSON only, with one complete JSON object per line and no Markdown.\n"
                       + "Start with {\"event\":\"start\",\"mode\":\"identified_translation\","
                       + "\"source_language\":\"[SourceLang]\",\"target_language\":\"[TargetLang]\"}.\n"
                       + "For every requested OCR block, emit exactly one line in request order: "
                       + "{\"event\":\"translation_delta\",\"id\":\"block-0\",\"text\":\"translated text\"}.\n"
                       + "The id must be copied exactly from the input. Put only that block's translated replacement "
                       + "text in text. Do not nest JSON inside text.\n"
                       + "Finish with exactly {\"event\":\"done\"}.\n";
        return ApplyLanguages(prompt + contract, source, target);
    }

    private string CreateSuppliedContractPrompt(
        TranslationLanguage source,
        TranslationLanguage target,
        string runtimeContract)
    {
        var prompt = ApplyLanguages(_promptTemplate, source, target);
        var contract = "\n\n# Runtime structured JSONL contract (highest priority)\n"
                       + "The supplied runtime contract below has higher priority than any earlier "
                       + "instruction. If an earlier instruction conflicts with it, ignore the "
                       + "conflicting part. Return raw NDJSON only, with one complete JSON object "
                       + "per line and no Markdown fences or explanatory text.\n"
                       + runtimeContract;
        return ApplyLanguages(prompt + contract, source, target);
    }

    private static string ApplyLanguages(
        string prompt,
        TranslationLanguage source,
        TranslationLanguage target)
    {
        var sourceName = string.IsNullOrEmpty(source.EnglishName) ? source.Id : source.EnglishName;
        var targetName = string.IsNullOrEmpty(target.EnglishName) ? target.Id : target.EnglishName;
        return prompt
            .Replace("[SourceLang]", sourceName, StringComparison.OrdinalIgnoreCase)
            .Replace("[TargetLang]", targetName, StringComparison.OrdinalIgnoreCase)
            .Replace("[\u6e90\u8bed\u8a00]", sourceName, StringComparison.OrdinalIgnoreCase)
            .Replace("[\u76ee\u6807\u8bed\u8a00]", targetName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadIdentifiedDelta(
        JsonElement item,
        out IdentifiedTranslationDelta delta)
    {
        delta = default!;
        if (!item.TryGetProperty("event", out var eventName)
            || !string.Equals(eventName.GetString(), "translation_delta", StringComparison.Ordinal)
            || !item.TryGetProperty("id", out var id)
            || !item.TryGetProperty("text", out var text))
        {
            return false;
        }

        var idValue = id.GetString();
        var textValue = text.GetString();
        if (string.IsNullOrWhiteSpace(idValue) || textValue is null)
            return false;

        delta = new IdentifiedTranslationDelta(idValue, textValue);
        return true;
    }

    private static void ValidateLanguages(TranslationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Source);
        ArgumentNullException.ThrowIfNull(request.Target);
    }

    private static string StripMarkdownFence(string value)
    {
        if (!value.StartsWith("```", StringComparison.Ordinal))
            return value;
        var firstLineEnd = value.IndexOf('\n');
        if (firstLineEnd >= 0)
            value = value[(firstLineEnd + 1)..];
        if (value.EndsWith("```", StringComparison.Ordinal))
            value = value[..^3];
        return value.Trim();
    }
}

internal sealed class StrictJsonLinesElementStreamDecoder
{
    private readonly StringBuilder _buffer = new();

    public IReadOnlyList<JsonElement> Append(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
            return [];

        _buffer.Append(chunk);
        var content = _buffer.ToString();
        var items = new List<JsonElement>();
        var start = 0;
        while (true)
        {
            var newline = content.IndexOf('\n', start);
            if (newline < 0)
                break;
            ParseLine(content[start..newline], items);
            start = newline + 1;
        }

        if (start > 0)
        {
            _buffer.Clear();
            _buffer.Append(content[start..]);
        }
        return items;
    }

    public IReadOnlyList<JsonElement> Complete()
    {
        var remaining = _buffer.ToString();
        _buffer.Clear();
        var items = new List<JsonElement>();
        ParseLine(remaining, items);
        return items;
    }

    private static void ParseLine(string value, List<JsonElement> destination)
    {
        var line = value.Trim();
        if (line.Length == 0)
            return;
        if (line.StartsWith("```", StringComparison.Ordinal))
            throw new JsonException("Markdown fences are not valid structured JSONL output.");

        try
        {
            destination.Add(JsonSerializer.Deserialize<JsonElement>(line));
        }
        catch (JsonException exception)
        {
            throw new JsonException("Invalid structured JSONL output.", exception);
        }
    }
}
