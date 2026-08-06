using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using EasyChat.Application.Translation;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.TextAssist;
using EasyChat.Contracts.Translation;
using EasyChat.Shared.Streaming;
using Microsoft.Extensions.Logging;

namespace EasyChat.Application.TextAssist;

public sealed class TextAssistUseCases : ITextAssistUseCases
{
    private readonly ISettingsUseCases _settings;
    private readonly ITranslationLanguageCatalog _languages;
    private readonly ITranslationUseCases _translation;
    private readonly ConfiguredTranslationProviderResolver _providers;
    private readonly ILogger<TextAssistUseCases> _logger;

    public TextAssistUseCases(
        ISettingsUseCases settings,
        ITranslationLanguageCatalog languages,
        ITranslationUseCases translation,
        ITranslationProviderFactory providerFactory,
        TranslationMessages messages,
        ILogger<TextAssistUseCases> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _languages = languages ?? throw new ArgumentNullException(nameof(languages));
        _translation = translation ?? throw new ArgumentNullException(nameof(translation));
        _providers = new ConfiguredTranslationProviderResolver(settings, providerFactory, messages);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public TextAssistProfile ResolveProfile(TextAssistOperation operation)
    {
        var settings = _settings.Current;
        var general = settings.General;
        var config = settings.TextAssist;
        var requiresAi = operation != TextAssistOperation.Translation;
        var promptId = operation switch
        {
            TextAssistOperation.Correction => config.CorrectionPromptId,
            TextAssistOperation.Polish => config.PolishPromptId,
            TextAssistOperation.Summary => config.SummaryPromptId,
            TextAssistOperation.Explanation => config.SummaryPromptId,
            _ => config.TranslationPromptId
        };

        if (config.FollowGlobal)
        {
            var provider = requiresAi
                ? TranslationEngineNames.AiModel
                : general.TranslationEngine ?? TranslationEngineNames.AiModel;
            return new TextAssistProfile(
                Map(general.SourceLanguage),
                Map(general.TargetLanguage),
                provider,
                general.AiModelId,
                general.MachineTranslationId ?? general.MachineTranslation,
                UsesGlobalConfiguration: true,
                PromptId: ResolvePromptId(promptId),
                DetailedExplanation: operation == TextAssistOperation.Translation
                                     && config.DetailedExplanation
                                     && IsAiProvider(provider));
        }

        var model = settings.AiModel.ConfiguredModels.FirstOrDefault(candidate =>
                        string.Equals(candidate.Id, config.AiModelId, StringComparison.Ordinal))
                    ?? settings.AiModel.ConfiguredModels.FirstOrDefault();
        if (!string.Equals(config.AiModelId, model?.Id, StringComparison.Ordinal))
            PersistResolvedModel(config, model?.Id);
        var selectedProvider = requiresAi ? TranslationEngineNames.AiModel : config.Provider;
        return new TextAssistProfile(
            _languages.Get(config.SourceLanguageId),
            _languages.Get(config.TargetLanguageId),
            selectedProvider,
            model?.Id,
            config.MachineProvider,
            UsesGlobalConfiguration: false,
            PromptId: ResolvePromptId(promptId),
            DetailedExplanation: operation == TextAssistOperation.Translation
                                 && config.DetailedExplanation
                                 && IsAiProvider(selectedProvider));
    }

    public IAsyncEnumerable<TextAssistEvent> StreamAsync(
        TextAssistRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profile = request.Profile ?? ResolveProfile(request.Operation);
        return request.Operation switch
        {
            TextAssistOperation.Translation => StreamTranslationAsync(request.Text, profile, cancellationToken),
            TextAssistOperation.Correction => StreamCorrectionAsync(request.Text, profile, cancellationToken),
            TextAssistOperation.Polish => StreamPolishAsync(request.Text, profile, cancellationToken),
            TextAssistOperation.Summary => StreamSummaryAsync(request.Text, profile, cancellationToken),
            TextAssistOperation.Explanation => StreamExplanationAsync(request.Text, profile, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Operation, null)
        };
    }

    private async IAsyncEnumerable<TextAssistEvent> StreamTranslationAsync(
        string text,
        TextAssistProfile profile,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Text assist translation profile: source={SourceId} ({SourceName}), target={TargetId} ({TargetName}), provider={Provider}",
            profile.Source.Id,
            profile.Source.EnglishName,
            profile.Target.Id,
            profile.Target.EnglishName,
            profile.Provider);
        yield return new TextAssistStartedEvent(
            "translation",
            profile.Source.EnglishName,
            profile.Target.EnglishName);

        if (profile.DetailedExplanation)
        {
            await foreach (var item in StreamDetailedTranslationAsync(text, profile, cancellationToken)
                               .ConfigureAwait(false))
                yield return item;
            yield break;
        }

        var (machineId, machineName) = ResolveMachineProvider(profile.MachineProvider);
        var selection = new TranslationProviderSelection(
            profile.Provider,
            AiModelId: profile.AiModelId,
            AiModelName: profile.UsesGlobalConfiguration ? _settings.Current.General.AiModel : null,
            MachineProviderId: IsMachineProvider(profile.Provider) ? machineId : null,
            MachineProviderName: IsMachineProvider(profile.Provider) ? machineName : null,
            PromptOverride: BuildTranslationPrompt(profile));
        var prepared = _translation.Prepare(selection);
        try
        {
            await foreach (var item in prepared.StreamAsync(
                                   new TranslationRequest(text, profile.Source, profile.Target, PlainText: true),
                                   cancellationToken).ConfigureAwait(false))
            {
                if (item is TranslationDeltaEvent delta && !string.IsNullOrEmpty(delta.Text))
                    yield return new TextAssistTranslationDeltaEvent(delta.Text);
            }
        }
        finally
        {
            if (prepared is IDisposable disposable)
                disposable.Dispose();
        }

        yield return new TextAssistCompletedEvent();
    }

    private async IAsyncEnumerable<TextAssistEvent> StreamDetailedTranslationAsync(
        string text,
        TextAssistProfile profile,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var provider = CreateChatProvider(profile);
        var annotationLanguage = ResolveOutputLanguage();
        var prompt = BuildDetailedTranslationPrompt(profile)
            .Replace("[SourceLang]", profile.Source.EnglishName, StringComparison.Ordinal)
            .Replace("[TargetLang]", profile.Target.EnglishName, StringComparison.Ordinal)
            .Replace("[AnnotationLanguage]", annotationLanguage, StringComparison.Ordinal);
        await foreach (var item in StreamStructuredAsync(
                           provider,
                           new ChatTranslationProviderRequest(
                               prompt,
                               text,
                               Temperature: 0.2f,
                               MaxOutputTokenCount: 5000,
                               ReasoningEffort: ChatReasoningEffort.Low),
                           "translation_delta",
                           "Empty detailed translation event.",
                           fallbackMode: null,
                           fallbackLanguage: profile.Source.EnglishName,
                           cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    private async IAsyncEnumerable<TextAssistEvent> StreamCorrectionAsync(
        string text,
        TextAssistProfile profile,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var outputLanguage = ResolveOutputLanguage();
        var prompt = BuildCorrectionPrompt(profile, """
# Role
You are a meticulous grammar, spelling, word-choice, and style editor.

# Task
Review the user's text in [Language].
The corrected text and all alternative expressions must remain in [Language].
Issue messages, suggestions, and the translations shown below each corrected
version must be written in [OutputLanguage], matching the user's native language.
Report every meaningful issue with UTF-16 `start` and `length` offsets into the original text.
Then provide a complete corrected version in [Language], followed by its translation in [OutputLanguage].
When a meaningful alternative expression exists, provide up to two additional
complete corrected versions in [Language]. The first version must be
the direct correction; alternatives should preserve the meaning while using
different natural wording. If no alternative is useful, emit only variant 1.

# Output protocol
Return raw NDJSON only, one JSON object per line, no Markdown fences.
Emit exactly this order:
{"event":"start","mode":"correction","language":"[LanguageId]"}
Zero or more {"event":"issue","start":0,"length":1,"category":"grammar|spelling|word_choice|style","message":"...","suggestion":"..."}
One or more {"event":"corrected_delta","variant":1,"text":"..."} objects whose concatenated text is the complete corrected version in [Language].
Optional variants 2 and 3 use their own concatenated corrected_delta sequence.
After each corrected version, emit one or more {"event":"correction_translation_delta","variant":1,"text":"..."} objects containing its translation in [OutputLanguage].
{"event":"done"}
""")
            .Replace("[Language]", profile.Source.EnglishName, StringComparison.Ordinal)
            .Replace("[LanguageId]", profile.Source.Id, StringComparison.Ordinal)
            .Replace("[OutputLanguage]", outputLanguage, StringComparison.Ordinal)
            + BuildOutputLanguageDirective(outputLanguage);
        await foreach (var item in StreamStructuredAsync(
                           CreateChatProvider(profile),
                           new ChatTranslationProviderRequest(
                               prompt,
                               text,
                               Temperature: 0.1f,
                               MaxOutputTokenCount: 4000),
                           "corrected_delta",
                           "Empty text assist event.",
                           fallbackMode: "correction",
                           fallbackLanguage: profile.Source.EnglishName,
                           cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    private async IAsyncEnumerable<TextAssistEvent> StreamPolishAsync(
        string text,
        TextAssistProfile profile,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nativeLanguage = ResolveOutputLanguage();
        var prompt = $$"""
# Role
You are a precise writing editor.

# Task
Polish the user's text while preserving its meaning and input language.
Detect the input language yourself unless the configured language is explicitly {{profile.Source.EnglishName}}.
After the polished text, explain the meaningful changes in {{nativeLanguage}}.
For each explanation, quote only the shortest useful original and revised snippets.
Do not invent changes, and omit explanations when no meaningful change was made.

# Optional user guidance
{{BuildAssistGuidance(profile)}}

# Output protocol
Return raw NDJSON only, one JSON object per line, without Markdown fences.
Emit exactly this order:
{"event":"start","mode":"polish","language":"{{profile.Source.Id}}"}
One or more {"event":"translation_delta","text":"..."} objects whose concatenated text is the complete polished result.
Zero or more {"event":"polish_explanation","category":"a short category in {{nativeLanguage}}","original":"...","revised":"...","explanation":"a concise explanation in {{nativeLanguage}}"}
{"event":"done"}
""";
        await foreach (var item in StreamStructuredAsync(
                           CreateChatProvider(profile),
                           new ChatTranslationProviderRequest(
                               prompt,
                               text,
                               Temperature: 0.2f,
                               MaxOutputTokenCount: 4000),
                           "translation_delta",
                           "Empty polish event.",
                           fallbackMode: null,
                           fallbackLanguage: profile.Source.EnglishName,
                           cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    private async IAsyncEnumerable<TextAssistEvent> StreamSummaryAsync(
        string text,
        TextAssistProfile profile,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nativeLanguage = ResolveOutputLanguage();
        var instruction = $"First create a concise summary of the user's text, then translate that summary into {nativeLanguage}. Detect the input language yourself. Output only the final {nativeLanguage} summary, with no label or commentary.";
        var prompt = $$"""
# Role
You are a precise writing assistant.

# Task
{{instruction}}
Use Markdown inline emphasis, lists, code spans, or blockquotes when they improve readability; do not wrap the entire response in a code fence.

# Optional user guidance
{{BuildAssistGuidance(profile)}}
""";
        var emitted = false;
        await foreach (var chunk in CreateChatProvider(profile).StreamAsync(
                           new ChatTranslationProviderRequest(
                               prompt,
                               text,
                               Temperature: 0.2f,
                               MaxOutputTokenCount: 4000),
                           cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrEmpty(chunk))
                continue;
            emitted = true;
            yield return new TextAssistTranslationDeltaEvent(chunk);
        }
        if (!emitted)
            yield return new TextAssistTranslationDeltaEvent(string.Empty);
        yield return new TextAssistCompletedEvent();
    }

    private async IAsyncEnumerable<TextAssistEvent> StreamExplanationAsync(
        string text,
        TextAssistProfile profile,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var outputLanguage = ResolveOutputLanguage();
        var prompt = $$"""
# Role
You are a precise language and context explainer.

# Task
Explain the selected text in {{outputLanguage}}. Detect the input language yourself.
Clarify its meaning in context, important terms, idioms, ambiguity, and implied intent when relevant.
Be concise but complete. Do not translate mechanically unless a translation helps the explanation.
Use Markdown inline emphasis, lists, code spans, or blockquotes when they improve readability; do not wrap the entire response in a code fence.
Output only the explanation, without a heading or meta commentary.

# Optional user guidance
{{BuildAssistGuidance(profile)}}
""";
        var emitted = false;
        await foreach (var chunk in CreateChatProvider(profile).StreamAsync(
                           new ChatTranslationProviderRequest(
                               prompt,
                               text,
                               Temperature: 0.2f,
                               MaxOutputTokenCount: 4000),
                           cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrEmpty(chunk))
                continue;
            emitted = true;
            yield return new TextAssistTranslationDeltaEvent(chunk);
        }
        if (!emitted)
            yield return new TextAssistTranslationDeltaEvent(string.Empty);
        yield return new TextAssistCompletedEvent();
    }

    private async IAsyncEnumerable<TextAssistEvent> StreamStructuredAsync(
        IChatTranslationProvider provider,
        ChatTranslationProviderRequest request,
        string deltaEvent,
        string emptyEventMessage,
        string? fallbackMode,
        string fallbackLanguage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var decoder = new JsonLinesDeltaStreamDecoder<TextAssistEvent>(
            line => JsonSerializer.Deserialize<TextAssistEvent>(line, options)
                    ?? throw new JsonException(emptyEventMessage),
            deltaEvent,
            "text",
            (exception, line) => _logger.LogDebug(
                exception,
                "Ignoring invalid text assist event: {Line}",
                line));
        var rawResponse = new StringBuilder();
        var emittedEvent = false;
        await foreach (var chunk in provider.StreamAsync(request, cancellationToken).ConfigureAwait(false))
        {
            rawResponse.Append(chunk);
            foreach (var item in decoder.Append(chunk))
            {
                emittedEvent = true;
                yield return item;
            }
        }
        foreach (var item in decoder.Complete())
        {
            emittedEvent = true;
            yield return item;
        }

        if (emittedEvent)
            yield break;
        var fallback = StripMarkdownFence(rawResponse.ToString().Trim());
        if (fallbackMode is not null)
        {
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                yield return new TextAssistStartedEvent(fallbackMode, fallbackLanguage, null);
                yield return new TextAssistCorrectedDeltaEvent(fallback);
                yield return new TextAssistCompletedEvent();
            }
            yield break;
        }
        if (!string.IsNullOrWhiteSpace(fallback))
            yield return new TextAssistTranslationDeltaEvent(fallback);
        yield return new TextAssistCompletedEvent();
    }

    private IChatTranslationProvider CreateChatProvider(TextAssistProfile profile) =>
        _providers.CreatePreferredAi(
            profile.AiModelId,
            useGlobalFallback: profile.UsesGlobalConfiguration,
            useFirstFallback: !profile.UsesGlobalConfiguration).Provider;

    private string ResolveOutputLanguage()
    {
        var general = _settings.Current.General;
        return general.NativeLanguage?.EnglishName ?? general.TargetLanguage.EnglishName;
    }

    private string BuildTranslationPrompt(TextAssistProfile profile) =>
        _providers.ResolvePrompt(profile.PromptId) + """

# Runtime translation contract
Source language: [SourceLang]
Target language: [TargetLang]
Translate from the source language to the target language exactly.
Only output the target-language translation. Do not output explanations, labels, analysis, or the source text.
The translated text must be plain text for direct input delivery. Do not use Markdown formatting, headings, list markers, or code fences.
""";

    private string BuildDetailedTranslationPrompt(TextAssistProfile profile) => """
# Role
You are a professional translator and language-learning annotator.

# User-selected translation guidance (secondary)
""" + _providers.ResolvePrompt(profile.PromptId) + """

# Runtime detailed translation contract
Source language: [SourceLang]
Target language: [TargetLang]
Annotation language: [AnnotationLanguage]
Translate the input naturally, then explain the source-language vocabulary and expressions that materially help a reader understand or learn it.
The translation MUST be in [TargetLang]. All annotation meanings, notes, labels, and explanations MUST be in [AnnotationLanguage], matching the user's native language.

Return raw NDJSON only, one complete JSON object per line, with no Markdown fences or prose.
Emit exactly this order:
1. `{"event":"source_detected","language":"en"}` when the source language is auto-detected.
2. One or more `{"event":"translation_delta","text":"..."}` objects. Concatenating their text MUST produce only the complete translation.
3. Zero to twelve annotation objects:
   `{"event":"annotation","term":"source word or phrase","category":"important_word|uncommon_word|collocation|usage_tip","meaning":"concise meaning in [AnnotationLanguage]","note":"context, grammar, nuance, or collocation guidance in [AnnotationLanguage]","relatedTerms":["source-language related word or phrase"]}`
4. `{"event":"done"}`

Annotation rules:
- Cover important words, uncommon words, fixed collocations, contextual meanings, register, and easy-to-miss usage when relevant.
- Use `term` for the exact source-language word or phrase being explained. Use its dictionary lemma only when that makes lookup clearer.
- Every value in `relatedTerms` MUST also be a source-language word or phrase suitable for dictionary lookup.
- Do not repeat annotations or annotate trivial function words.
- `meaning` is required. Omit `note` or use an empty string only when no extra explanation is useful. Use an empty array when there are no related terms.
- The protocol above has priority over the user-selected guidance. Never emit text outside the documented NDJSON events.
""";

    private string BuildCorrectionPrompt(TextAssistProfile profile, string fallback) => """
# User-selected correction guidance
""" + (_providers.ResolveOptionalPrompt(profile.PromptId) ?? fallback) + """

# Runtime correction contract
The guidance above is secondary. You MUST follow this correction protocol even if it conflicts with the selected guidance.
Return raw NDJSON only, one JSON object per line, with no Markdown fences or prose.
Emit exactly this order:
{"event":"start","mode":"correction","language":"[LanguageId]"}
Zero or more {"event":"issue","start":0,"length":1,"category":"grammar|spelling|word_choice|style","message":"...","suggestion":"..."}
One or more {"event":"corrected_delta","variant":1,"text":"..."} objects whose concatenated text is the complete corrected version in [LanguageId].
Optional variants 2 and 3 use their own concatenated corrected_delta sequence.
After each corrected version, emit one or more {"event":"correction_translation_delta","variant":1,"text":"..."} objects containing its translation in [UiLanguage].
{"event":"done"}
""";

    private string BuildAssistGuidance(TextAssistProfile profile) =>
        _providers.ResolveOptionalPrompt(profile.PromptId) ?? string.Empty;

    private static string BuildOutputLanguageDirective(string outputLanguage) => """

# Final mandatory language rule
The corrected text MUST remain in the original source language.
Only issue messages, suggestions, and correction translations MUST be written in [OutputLanguage].
Every emitted corrected variant must be followed by its correction_translation_delta.
""".Replace("[OutputLanguage]", outputLanguage, StringComparison.Ordinal);

    private string? ResolvePromptId(string? promptId)
    {
        var prompts = _settings.Current.Prompts;
        if (!string.IsNullOrWhiteSpace(promptId)
            && prompts.Entries.Any(prompt => string.Equals(prompt.Id, promptId, StringComparison.Ordinal)))
            return promptId;
        return string.IsNullOrWhiteSpace(prompts.SelectedPromptId) ? null : prompts.SelectedPromptId;
    }

    private void PersistResolvedModel(TextAssistSettings current, string? modelId)
    {
        var settings = _settings.Current;
        _settings.Update(
            SettingsSection.TextAssist,
            settings with { TextAssist = current with { AiModelId = modelId } });
    }

    private (string? Id, string? Name) ResolveMachineProvider(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (null, MachineTranslationProviderNames.Baidu);
        var machine = _settings.Current.MachineTranslation;
        var isId = string.Equals(machine.Baidu.Id, value, StringComparison.Ordinal)
                   || string.Equals(machine.Tencent.Id, value, StringComparison.Ordinal)
                   || string.Equals(machine.Google.Id, value, StringComparison.Ordinal)
                   || string.Equals(machine.DeepL.Id, value, StringComparison.Ordinal);
        return isId ? (value, null) : (null, value);
    }

    private static TranslationLanguage Map(LanguageSettings language) => new(
        language.Id,
        language.EnglishName,
        language.ChineseName,
        language.ProviderCodes,
        language.Icon);

    private static bool IsAiProvider(string provider) =>
        string.Equals(provider, TranslationEngineNames.AiModel, StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "AI", StringComparison.OrdinalIgnoreCase);

    private static bool IsMachineProvider(string provider) =>
        string.Equals(provider, TranslationEngineNames.MachineTrans, StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "Machine", StringComparison.OrdinalIgnoreCase);

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
