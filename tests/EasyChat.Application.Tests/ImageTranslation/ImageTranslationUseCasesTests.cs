using System.Runtime.CompilerServices;
using EasyChat.Application.ImageTranslation;
using EasyChat.Application.Tests.Settings;
using EasyChat.Contracts.ImageTranslation;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Translation;
using EasyChat.Shared.Results;

namespace EasyChat.Application.Tests.ImageTranslation;

[TestClass]
public sealed class ImageTranslationUseCasesTests
{
    [TestMethod]
    public async Task TranslateAsync_PreservesMasterRegionOrderingAndRendererBoundary()
    {
        var renderer = new FakeRenderer();
        var settings = SettingsTestData.CreateBundle() with
        {
            General = SettingsTestData.CreateBundle().General with
            {
                TranslationEngine = TranslationEngineNames.MachineTrans
            }
        };
        var useCases = new ImageTranslationUseCases(
            new FakeTranslationUseCases(),
            new FakeSettingsUseCases(settings),
            renderer);
        var image = new ImageFrame(2, 2, 8, 96, 96, new byte[16]);
        var lower = Region("lower", 20, 30);
        var upper = Region("upper", 5, 10);

        var result = await useCases.TranslateAsync(new ImageTranslationRequest(
            image,
            new OcrRecognitionResult([lower, upper, Region(" ", 0, 0)]),
            null,
            new TranslationLanguage("en", "English")));

        Assert.AreSame(image, renderer.Source);
        Assert.HasCount(2, renderer.Overlays);
        Assert.AreEqual("upper", renderer.Overlays[0].Region.Text);
        Assert.AreEqual("translated:upper", renderer.Overlays[0].Translation);
        Assert.AreEqual(2, result.DetectedBlockCount);
        Assert.AreEqual(2, result.TranslatedBlockCount);
    }

    [TestMethod]
    public async Task TranslateRegionsAsync_FallsBackForMissingAiBatchItems()
    {
        var settings = SettingsTestData.CreateBundle() with
        {
            General = SettingsTestData.CreateBundle().General with
            {
                TranslationEngine = TranslationEngineNames.AiModel
            }
        };
        var useCases = new ImageTranslationUseCases(
            new PartialBatchTranslationUseCases(),
            new FakeSettingsUseCases(settings),
            new FakeRenderer());
        var recognition = new OcrRecognitionResult(
        [
            Region("first", 0, 0),
            Region("second", 20, 0)
        ]);

        var result = await useCases.TranslateRegionsAsync(
            new ImageRegionTranslationRequest(
                recognition,
                [0, 1],
                new TranslationLanguage("en", "English"),
                new TranslationLanguage("zh-Hans", "Chinese")));

        Assert.HasCount(2, result.Translations);
        Assert.AreEqual("batch:first", result.Translations.Single(item => item.RegionIndex == 0).Translation);
        Assert.AreEqual("fallback:second", result.Translations.Single(item => item.RegionIndex == 1).Translation);
    }

    private static OcrTextRegion Region(string text, double x, double y) =>
        new(text,
        [
            new ImagePoint(x, y),
            new ImagePoint(x + 8, y),
            new ImagePoint(x + 8, y + 4),
            new ImagePoint(x, y + 4)
        ],
        0);

    private sealed class FakeRenderer : IImageTranslationRenderer
    {
        public ImageFrame? Source { get; private set; }
        public IReadOnlyList<ImageTranslationOverlay> Overlays { get; private set; } = [];

        public Task<ImageTranslationRenderResult> RenderAsync(
            ImageFrame background,
            IReadOnlyList<ImageTranslationOverlay> overlays,
            CancellationToken cancellationToken = default)
        {
            Source = background;
            Overlays = overlays;
            return Task.FromResult(new ImageTranslationRenderResult(
                background,
                [],
                overlays.Count));
        }
    }

    private sealed class FakeTranslationUseCases : ITranslationUseCases
    {
        public ITranslationSession Prepare(TranslationProviderSelection? provider = null) =>
            new FakeTranslationSession();

        public Task<Result<TranslationResponse>> TranslateAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<TranslationResponse>.Success(
                new TranslationResponse($"translated:{request.Text}")));

        public async IAsyncEnumerable<TranslationEvent> StreamAsync(
            TranslationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeTranslationSession : ITranslationSession
    {
        public bool SupportsIdentifiedStreaming => false;

        public Task<TranslationResponse> TranslateAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TranslationResponse($"translated:{request.Text}"));

        public async IAsyncEnumerable<TranslationEvent> StreamAsync(
            TranslationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<IdentifiedTranslationDelta> StreamIdentifiedAsync(
            TranslationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class PartialBatchTranslationUseCases : ITranslationUseCases
    {
        public ITranslationSession Prepare(TranslationProviderSelection? provider = null) =>
            provider?.PromptOverride is not null
                ? new PartialBatchSession()
                : new FallbackSession();

        public Task<Result<TranslationResponse>> TranslateAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async IAsyncEnumerable<TranslationEvent> StreamAsync(
            TranslationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class PartialBatchSession : ITranslationSession
    {
        public bool SupportsIdentifiedStreaming => true;

        public Task<TranslationResponse> TranslateAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async IAsyncEnumerable<TranslationEvent> StreamAsync(
            TranslationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<IdentifiedTranslationDelta> StreamIdentifiedAsync(
            TranslationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new IdentifiedTranslationDelta("block-0", "batch:first");
        }
    }

    private sealed class FallbackSession : ITranslationSession
    {
        public bool SupportsIdentifiedStreaming => true;

        public Task<TranslationResponse> TranslateAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TranslationResponse($"fallback:{request.Text}"));

        public async IAsyncEnumerable<TranslationEvent> StreamAsync(
            TranslationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<IdentifiedTranslationDelta> StreamIdentifiedAsync(
            TranslationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeSettingsUseCases(SettingsBundle current) : ISettingsUseCases
    {
        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<SettingsSaveFailedEventArgs>? SaveFailed
        {
            add { }
            remove { }
        }
        public bool IsInitialized => true;
        public SettingsBundle Current { get; } = current;

        public ValueTask<Result<SettingsBundle>> InitializeAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<SettingsBundle>.Success(Current));

        public Result Update(SettingsSection section, SettingsBundle settings) => Result.Success();
        public ValueTask<Result> FlushAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
