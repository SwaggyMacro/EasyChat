using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Translation;
using EasyChat.Shared.Results;

namespace EasyChat.Contracts.ImageTranslation;

public sealed record ImageTranslationRequest(
    ImageFrame Image,
    OcrRecognitionResult Recognition,
    TranslationLanguage? SourceLanguage,
    TranslationLanguage TargetLanguage);

public sealed record ImageTranslationResult(
    ImageFrame Image,
    IReadOnlyList<string> Warnings,
    int DetectedBlockCount,
    int TranslatedBlockCount);

public sealed record ImageTranslationOverlay(OcrTextRegion Region, string Translation);

public sealed record ImageTranslationRenderResult(
    ImageFrame Image,
    IReadOnlyList<string> Warnings,
    int RenderedBlockCount);

public sealed record ImageRegionTranslationRequest(
    OcrRecognitionResult Recognition,
    IReadOnlyList<int> RegionIndexes,
    TranslationLanguage? SourceLanguage,
    TranslationLanguage TargetLanguage);

public sealed record ImageRegionTranslation(
    int RegionIndex,
    string Translation);

public sealed record ImageRegionTranslationResult(
    IReadOnlyList<ImageRegionTranslation> Translations,
    IReadOnlyList<string> Warnings);

public sealed record ImageTranslationEditResult(
    ImageFrame Image,
    IReadOnlyList<string> Warnings,
    bool IsOriginal,
    bool CanUndo,
    bool CanRedo,
    int ActiveOverlayCount);

public interface IImageBackgroundCleaner
{
    ImageFrame RemoveText(
        ImageFrame source,
        IReadOnlyList<OcrTextRegion> regions,
        CancellationToken cancellationToken = default);
}

public interface IImageTranslationRenderer
{
    Task<ImageTranslationRenderResult> RenderAsync(
        ImageFrame background,
        IReadOnlyList<ImageTranslationOverlay> overlays,
        CancellationToken cancellationToken = default);
}

public interface IImageTranslationUseCases
{
    Task<ImageTranslationResult> TranslateAsync(
        ImageTranslationRequest request,
        CancellationToken cancellationToken = default);

    Task<ImageRegionTranslationResult> TranslateRegionsAsync(
        ImageRegionTranslationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IImageTranslationEditSession : IAsyncDisposable
{
    ImageFrame OriginalImage { get; }

    bool CanUndo { get; }

    bool CanRedo { get; }

    bool HasChanges { get; }

    Task<Result<ImageTranslationEditResult>> TranslateAsync(
        OcrRecognitionResult recognition,
        IReadOnlyList<int> regionIndexes,
        OcrLanguage sourceLanguage,
        CancellationToken cancellationToken = default);

    Task<Result<ImageTranslationEditResult>> UndoAsync(
        CancellationToken cancellationToken = default);

    Task<Result<ImageTranslationEditResult>> RedoAsync(
        CancellationToken cancellationToken = default);

    Task<Result<ImageTranslationEditResult>> RestoreOriginalAsync(
        CancellationToken cancellationToken = default);

    ValueTask ResetHistoryAsync(CancellationToken cancellationToken = default);
}

public interface IImageTranslationEditSessionFactory
{
    Result ValidateImage(int width, int height);

    Result<IImageTranslationEditSession> Create(ImageFrame originalImage);
}
