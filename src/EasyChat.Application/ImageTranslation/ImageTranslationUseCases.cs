using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyChat.Contracts.ImageTranslation;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Translation;

namespace EasyChat.Application.ImageTranslation;

public sealed class ImageTranslationUseCases : IImageTranslationUseCases
{
    private readonly ITranslationUseCases _translation;
    private readonly ISettingsUseCases _settings;
    private readonly IImageTranslationRenderer _renderer;

    public ImageTranslationUseCases(
        ITranslationUseCases translation,
        ISettingsUseCases settings,
        IImageTranslationRenderer renderer)
    {
        _translation = translation ?? throw new ArgumentNullException(nameof(translation));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public async Task<ImageTranslationResult> TranslateAsync(
        ImageTranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var blocks = CreateIndexedBlocks(request.Recognition.Regions);
        if (blocks.Count == 0)
            return new ImageTranslationResult(request.Image, ["No text detected."], 0, 0);

        var translated = await TranslateRegionsAsync(
            new ImageRegionTranslationRequest(
                request.Recognition,
                blocks.Select(block => block.Index).ToArray(),
                request.SourceLanguage,
                request.TargetLanguage),
            cancellationToken);
        var warnings = translated.Warnings.ToList();
        var overlays = translated.Translations
            .Select(item => new ImageTranslationOverlay(
                request.Recognition.Regions[item.RegionIndex],
                item.Translation))
            .ToArray();

        if (overlays.Length == 0)
            return new ImageTranslationResult(request.Image, warnings, blocks.Count, 0);

        var rendered = await _renderer.RenderAsync(request.Image, overlays, cancellationToken);
        warnings.AddRange(rendered.Warnings);
        return new ImageTranslationResult(
            rendered.Image,
            warnings,
            blocks.Count,
            rendered.RenderedBlockCount);
    }

    public async Task<ImageRegionTranslationResult> TranslateRegionsAsync(
        ImageRegionTranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Recognition);
        ArgumentNullException.ThrowIfNull(request.RegionIndexes);
        ArgumentNullException.ThrowIfNull(request.TargetLanguage);
        cancellationToken.ThrowIfCancellationRequested();

        var indexes = request.RegionIndexes
            .Distinct()
            .ToHashSet();
        if (indexes.Any(index => index < 0 || index >= request.Recognition.Regions.Count))
            throw new ArgumentOutOfRangeException(nameof(request), "A selected OCR region index is invalid.");

        var allBlocks = CreateIndexedBlocks(request.Recognition.Regions);
        var selected = allBlocks.Where(block => indexes.Contains(block.Index)).ToArray();
        if (selected.Length == 0)
            return new ImageRegionTranslationResult([], ["No selected text could be translated."]);

        var warnings = new List<string>();
        var translations = string.Equals(
                _settings.Current.General.TranslationEngine,
                TranslationEngineNames.AiModel,
                StringComparison.OrdinalIgnoreCase)
            ? await TranslateWithAiAsync(
                allBlocks,
                selected,
                request.SourceLanguage,
                request.TargetLanguage,
                warnings,
                cancellationToken)
            : await TranslateWithMachineProviderAsync(
                selected,
                request.SourceLanguage,
                request.TargetLanguage,
                warnings,
                cancellationToken);
        return new ImageRegionTranslationResult(translations, warnings);
    }

    internal static IReadOnlyList<OcrTextRegion> CreateBlocks(
        IReadOnlyList<OcrTextRegion> regions) =>
        CreateIndexedBlocks(regions)
            .Select(block => block.Region)
            .ToArray();

    private static IReadOnlyList<IndexedBlock> CreateIndexedBlocks(
        IReadOnlyList<OcrTextRegion> regions) =>
        regions
            .Select((region, index) => new IndexedBlock(index, region))
            .Where(block => !string.IsNullOrWhiteSpace(block.Region.Text)
                            && block.Region.Polygon.Count >= 3)
            .OrderBy(block => block.Region.Polygon.Min(point => point.Y))
            .ThenBy(block => block.Region.Polygon.Min(point => point.X))
            .ToArray();

    private async Task<IReadOnlyList<ImageRegionTranslation>> TranslateWithAiAsync(
        IReadOnlyList<IndexedBlock> allBlocks,
        IReadOnlyList<IndexedBlock> selectedBlocks,
        TranslationLanguage? sourceLanguage,
        TranslationLanguage targetLanguage,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var items = allBlocks
            .Select(block => new BatchTranslationItem(
                $"block-{block.Index}",
                block.Region.Text.Trim()))
            .ToArray();
        var settings = _settings.Current.General;
        var provider = !string.IsNullOrWhiteSpace(settings.AiModelId)
            ? new TranslationProviderSelection(
                TranslationEngineNames.AiModel,
                AiModelId: settings.AiModelId,
                PromptOverride: ImageBatchPrompt)
            : new TranslationProviderSelection(
                TranslationEngineNames.AiModel,
                AiModelName: settings.AiModel ?? "OpenAI",
                PromptOverride: ImageBatchPrompt);

        var translations = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        try
        {
            var session = _translation.Prepare(provider);
            using var disposable = session as IDisposable;
            if (!session.SupportsIdentifiedStreaming)
                throw new InvalidOperationException(
                    "The configured AI translator does not support identified streams.");

            var payload = JsonSerializer.Serialize(new BatchTranslationPayload(
                items,
                selectedBlocks.Select(block => $"block-{block.Index}").ToArray()));
            var requestedIds = selectedBlocks
                .Select(block => $"block-{block.Index}")
                .ToHashSet(StringComparer.Ordinal);
            await foreach (var delta in session.StreamIdentifiedAsync(
                               new TranslationRequest(
                                   payload,
                                   sourceLanguage,
                                   targetLanguage),
                               cancellationToken))
            {
                if (!requestedIds.Contains(delta.Id) || string.IsNullOrEmpty(delta.Text))
                    continue;

                if (!translations.TryGetValue(delta.Id, out var text))
                {
                    text = new StringBuilder();
                    translations.Add(delta.Id, text);
                }

                text.Append(delta.Text);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }

        var missing = selectedBlocks
            .Where(block => !translations.TryGetValue($"block-{block.Index}", out var value)
                            || string.IsNullOrWhiteSpace(value.ToString()))
            .ToArray();
        if (missing.Length > 0)
        {
            var fallback = _translation.Prepare();
            using var fallbackDisposable = fallback as IDisposable;
            foreach (var block in missing)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var response = await fallback.TranslateAsync(
                        new TranslationRequest(
                            block.Region.Text.Trim(),
                            sourceLanguage,
                            targetLanguage),
                        cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(response.Text))
                    {
                        translations[$"block-{block.Index}"] =
                            new StringBuilder(response.Text.Trim());
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                }
            }
        }

        return CreateTranslations(selectedBlocks, translations, warnings);
    }

    private async Task<IReadOnlyList<ImageRegionTranslation>> TranslateWithMachineProviderAsync(
        IReadOnlyList<IndexedBlock> blocks,
        TranslationLanguage? sourceLanguage,
        TranslationLanguage targetLanguage,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var session = _translation.Prepare();
        using var disposable = session as IDisposable;
        var translations = new List<ImageRegionTranslation>(blocks.Count);
        foreach (var block in blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var response = await session.TranslateAsync(
                    new TranslationRequest(
                        block.Region.Text.Trim(),
                        sourceLanguage,
                        targetLanguage),
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(response.Text))
                    warnings.Add($"Unable to translate: {block.Region.Text.Trim()}");
                else
                    translations.Add(new ImageRegionTranslation(
                        block.Index,
                        response.Text.Trim()));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                warnings.Add($"Unable to translate: {block.Region.Text.Trim()}");
            }
        }

        return translations;
    }

    private static IReadOnlyList<ImageRegionTranslation> CreateTranslations(
        IReadOnlyList<IndexedBlock> blocks,
        IReadOnlyDictionary<string, StringBuilder> translations,
        List<string> warnings)
    {
        var results = new List<ImageRegionTranslation>(blocks.Count);
        foreach (var block in blocks)
        {
            var id = $"block-{block.Index}";
            if (translations.TryGetValue(id, out var value)
                && !string.IsNullOrWhiteSpace(value.ToString()))
            {
                results.Add(new ImageRegionTranslation(block.Index, value.ToString().Trim()));
            }
            else
            {
                warnings.Add($"Unable to translate: {block.Region.Text.Trim()}");
            }
        }

        return results;
    }

    private sealed record IndexedBlock(int Index, OcrTextRegion Region);

    private sealed record BatchTranslationItem(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("text")] string Text);

    private sealed record BatchTranslationPayload(
        [property: JsonPropertyName("items")] IReadOnlyList<BatchTranslationItem> Items,
        [property: JsonPropertyName("translate_ids")] IReadOnlyList<string> TranslateIds);

    private const string ImageBatchPrompt = """
You translate all OCR text blocks from one image together so that shared visual context,
terminology, labels, and sentence fragments remain consistent.

The user input is a JSON object with this exact shape:
{"items":[{"id":"block-0","text":"source text"}],"translate_ids":["block-0"]}

Use every object in `items` as shared image context. Translate only the objects whose IDs
appear in `translate_ids` from [SourceLang] to [TargetLang]. The runtime's identified JSONL
contract defines the response format. Emit one outer `translation_delta` event for every
requested block, with the original ID in `id` and its translated replacement text in `text`.

Rules:
- Preserve every requested `id` exactly and return exactly one result for every `translate_ids` entry.
- Keep the output items in the same order as `translate_ids`.
- Translate only the `text` values. Never translate or alter an `id`.
- Use the complete `items` collection as context, but do not return unrequested IDs.
- Do not merge, split, omit, or invent requested items.
- Each `translation` must contain only the target-language replacement text for that block.
- Do not translate the input JSON as prose. It is control data describing separate OCR blocks.
- Do not nest JSON inside `text` and do not emit Markdown or explanations.
""";
}
