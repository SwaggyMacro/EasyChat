using System.Runtime.CompilerServices;
using System.Text.Json;
using EasyChat.Application.Translation;
using EasyChat.Contracts.SelectionTranslation;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Translation;
using EasyChat.Shared.Streaming;
using Microsoft.Extensions.Logging;

namespace EasyChat.Application.SelectionTranslation;

public sealed class SelectionTranslationUseCases : ISelectionTranslationUseCases
{
    private const string SystemPromptTemplate = """
# Role
You are a professional translator and lexicographer proficient in [SourceLang] and [TargetLang].

# Task
Source Language: [SourceLang]
Target Language: [TargetLang]
If Source Language is "Auto" or "Auto Detect", detect the input language.
All translations, meanings, tips, and grammatical labels MUST be in [TargetLang].

# Mode Selection (strict)
First trim whitespace and trailing punctuation.
- For space-based languages: no internal spaces means word mode; one or more spaces means sentence mode.
- For Chinese, Japanese, and other no-space languages: 4 characters or fewer means word mode; otherwise sentence mode.
Do not override these rules based on meaning or grammar.

# Output Protocol: JSON Lines
Return raw NDJSON only: one complete JSON object per line, with no prose outside the documented JSONL events.
Markdown is allowed inside translation, meaning, tips, and other text fields when it improves readability.
Never wrap the JSONL response in a Markdown code fence.
Every line must contain the `event` property shown below. Escape all JSON strings correctly.
Emit events in exactly the documented order and always finish with `{"event":"done"}`.

## Sentence mode
1. `{"event":"start","mode":"sentence"}`
2. `{"event":"source_detected","language":"en"}`
3. One or more `{"event":"translation_delta","text":"..."}` events. Split the complete translation into natural short phrases so it can be rendered while you generate it. Concatenating `text` values must be the complete fluent translation.
4. Zero to three `{"event":"keyword","word":"original term","meaning":"meaning in [TargetLang]"}` events.
5. `{"event":"done"}`

## Word mode
1. `{"event":"start","mode":"word"}`
2. `{"event":"source_detected","language":"en"}`
3. `{"event":"word_header","word":"lemma or original word","phonetic":"IPA or pronunciation"}`
4. One or more `{"event":"definition","pos":"n.","meaning":"meaning in [TargetLang]"}` events.
5. Zero or more `{"event":"form","label":"form name in [TargetLang]","word":"word form"}` events.
6. Optionally one `{"event":"tips","text":"usage tips in [TargetLang]"}` event.
7. Exactly three `{"event":"example","origin":"original sentence","translation":"translation in [TargetLang]"}` events.
8. `{"event":"done"}`
""";

    private readonly ISettingsUseCases _settings;
    private readonly ConfiguredTranslationProviderResolver _providers;
    private readonly ILogger<SelectionTranslationUseCases> _logger;

    public SelectionTranslationUseCases(
        ISettingsUseCases settings,
        ITranslationProviderFactory providerFactory,
        TranslationMessages messages,
        ILogger<SelectionTranslationUseCases> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _providers = new ConfiguredTranslationProviderResolver(settings, providerFactory, messages);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SelectionTranslationResult> TranslateAsync(
        SelectionTranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = UsesMachineProvider()
            ? SelectionTranslationSource.Machine
            : SelectionTranslationSource.Ai;
        var accumulator = new SelectionTranslationResultAccumulator(request.Text, source);
        await foreach (var item in StreamAsync(request, cancellationToken).ConfigureAwait(false))
            accumulator.Apply(item);
        return accumulator.Build();
    }

    public IAsyncEnumerable<SelectionTranslationEvent> StreamAsync(
        SelectionTranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return UsesMachineProvider()
            ? StreamMachineAsync(request, cancellationToken)
            : StreamAiAsync(request, forceWordMode: false, cancellationToken);
    }

    public IAsyncEnumerable<SelectionTranslationEvent> StreamDictionaryAsync(
        SelectionTranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StreamAiAsync(request, forceWordMode: true, cancellationToken);
    }

    private async IAsyncEnumerable<SelectionTranslationEvent> StreamAiAsync(
        SelectionTranslationRequest request,
        bool forceWordMode,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var config = _settings.Current.SelectionTranslation;
        var resolved = _providers.CreatePreferredAi(
            config.AiModelId,
            useGlobalFallback: true,
            useFirstFallback: true);
        PersistResolvedModel(config, resolved.Configuration.Id);

        var configuredPrompt = _providers.ResolveOptionalPrompt(config.PromptId);
        var prompt = SystemPromptTemplate
                     + (string.IsNullOrWhiteSpace(configuredPrompt)
                         ? string.Empty
                         : "\n\n# User-selected guidance (secondary)\n" + configuredPrompt)
                     + """

# Runtime selection contract
Source language: [SourceLang]
Target language: [TargetLang]
The JSONL protocol above has the highest priority. If the user-selected guidance
conflicts with it, ignore the conflicting guidance. Use the selected languages
exactly. Return only the documented JSONL events; never add prose outside JSONL.
""";
        if (forceWordMode)
        {
            prompt += """

# Forced dictionary lookup
Ignore the automatic mode-selection rules for this request and use word mode.
Treat the complete input as one dictionary term or collocation, even when it contains spaces.
Emit the documented word-mode events only; never emit sentence-mode translation_delta or keyword events.
""";
        }

        prompt = prompt
            .Replace("[SourceLang]", request.Source.EnglishName, StringComparison.Ordinal)
            .Replace("[TargetLang]", request.Target.EnglishName, StringComparison.Ordinal);
        _logger.LogInformation(
            "Streaming selection translation: {Source} -> {Target}, Length={Length}, ForceWordMode={ForceWordMode}",
            request.Source.EnglishName,
            request.Target.EnglishName,
            request.Text.Length,
            forceWordMode);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var decoder = new JsonLinesDeltaStreamDecoder<SelectionTranslationEvent>(
            line => JsonSerializer.Deserialize<SelectionTranslationEvent>(line, options)
                    ?? throw new JsonException("Empty structured translation event."),
            "translation_delta",
            "text");
        await foreach (var chunk in resolved.Provider.StreamAsync(
                           new ChatTranslationProviderRequest(
                               prompt,
                               request.Text,
                               Temperature: 0.3f,
                               MaxOutputTokenCount: 4000,
                               ReasoningEffort: ChatReasoningEffort.Low),
                           cancellationToken).ConfigureAwait(false))
        {
            foreach (var item in decoder.Append(chunk))
                yield return item;
        }

        foreach (var item in decoder.Complete())
            yield return item;
    }

    private async IAsyncEnumerable<SelectionTranslationEvent> StreamMachineAsync(
        SelectionTranslationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var providerName = _settings.Current.SelectionTranslation.MachineProvider
                           ?? MachineTranslationProviderNames.Baidu;
        var resolved = _providers.CreateMachine(null, providerName);
        _logger.LogInformation("Using machine selection translation provider: {Provider}", resolved.Configuration.Name);
        var translated = await resolved.Provider.TranslateAsync(
            new TranslationProviderRequest(
                request.Text,
                ResolveLanguageCode(request.Source, resolved.Configuration.Name),
                ResolveLanguageCode(request.Target, resolved.Configuration.Name)),
            cancellationToken).ConfigureAwait(false);

        if (!request.Text.Trim().Contains(' ') && request.Text.Length < 20)
        {
            yield return new SelectionTranslationStartedEvent(SelectionTranslationMode.Word);
            yield return new SelectionTranslationWordHeaderEvent(request.Text, null);
            yield return new SelectionTranslationDefinitionEvent(string.Empty, translated);
        }
        else
        {
            yield return new SelectionTranslationStartedEvent(SelectionTranslationMode.Sentence);
            yield return new SelectionTranslationDeltaEvent(translated);
        }
        yield return new SelectionTranslationCompletedEvent();
    }

    private bool UsesMachineProvider()
    {
        var provider = _settings.Current.SelectionTranslation.Provider;
        return string.Equals(provider, TranslationEngineNames.MachineTrans, StringComparison.OrdinalIgnoreCase)
               || string.Equals(provider, "Machine", StringComparison.OrdinalIgnoreCase);
    }

    private void PersistResolvedModel(SelectionTranslationSettings current, string modelId)
    {
        if (string.Equals(current.AiModelId, modelId, StringComparison.Ordinal))
            return;

        var settings = _settings.Current;
        var update = settings with
        {
            SelectionTranslation = current with { AiModelId = modelId }
        };
        var result = _settings.Update(SettingsSection.SelectionTranslation, update);
        if (result.IsFailure)
            _logger.LogWarning("Unable to persist the resolved selection AI model: {Error}", result.Error.Message);
    }

    private static string ResolveLanguageCode(TranslationLanguage language, string providerName)
    {
        if (language.ProviderCodes is not null
            && language.ProviderCodes.TryGetValue(providerName, out var code)
            && !string.IsNullOrWhiteSpace(code))
        {
            return code;
        }
        return language.Id;
    }
}
