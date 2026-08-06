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
public sealed class ImageTranslationEditSessionTests
{
    [TestMethod]
    public async Task SessionReservesAndReleasesTwoImageBuffers()
    {
        var budget = new ImageTranslationMemoryBudget();
        var factory = CreateFactory(budget);
        var frame = Frame();

        var created = factory.Create(frame);

        Assert.IsTrue(created.IsSuccess);
        Assert.AreEqual(32, budget.RetainedBytes);
        await created.Value.DisposeAsync();
        Assert.AreEqual(0, budget.RetainedBytes);
    }

    [TestMethod]
    public async Task SessionRejectsNewWindowWhenAggregateBudgetIsFull()
    {
        var budget = new ImageTranslationMemoryBudget();
        Assert.IsTrue(budget.TryReserve(ImageTranslationMemoryBudget.MaximumRetainedBytes - 32));
        var factory = CreateFactory(budget);
        var first = factory.Create(Frame());
        var second = factory.Create(Frame());

        Assert.IsTrue(first.IsSuccess);
        Assert.IsTrue(second.IsFailure);
        Assert.AreEqual("image-translation.memory-budget-exceeded", second.Error.Code);

        await first.Value.DisposeAsync();
        budget.Release(ImageTranslationMemoryBudget.MaximumRetainedBytes - 32);
        Assert.AreEqual(0, budget.RetainedBytes);
    }

    [TestMethod]
    public void ValidationRejectsDecodedImagesLargerThan128MiB()
    {
        var factory = CreateFactory(new ImageTranslationMemoryBudget());

        var result = factory.ValidateImage(8192, 8192);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual("image-translation.image-too-large", result.Error.Code);
    }

    [TestMethod]
    public async Task HistoryIsDeltaOnlyAndEvictsPastOneHundredActions()
    {
        var budget = new ImageTranslationMemoryBudget();
        var translations = new IncrementingTranslations();
        var factory = CreateFactory(budget, translations);
        var session = factory.Create(Frame()).Value;
        var recognition = new OcrRecognitionResult([Region("source")]);

        for (var index = 0; index < 105; index++)
        {
            var result = await session.TranslateAsync(
                recognition,
                [0],
                OcrLanguages.English);
            Assert.IsTrue(result.IsSuccess);
        }

        var undoField = session.GetType().GetField(
            "_undo",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var undo = undoField?.GetValue(session)
                   ?? throw new AssertFailedException("Undo history field was not found.");
        var count = (int)(undo.GetType().GetProperty("Count")?.GetValue(undo) ?? -1);
        Assert.AreEqual(100, count);
        var deltaType = session.GetType().GetNestedType(
            "EditDelta",
            System.Reflection.BindingFlags.NonPublic)
                        ?? throw new AssertFailedException("Edit delta type was not found.");
        Assert.IsFalse(deltaType.GetProperties().Any(property =>
            property.PropertyType == typeof(ImageFrame)
            || property.PropertyType == typeof(ReadOnlyMemory<byte>)
            || property.PropertyType == typeof(byte[])));

        await session.DisposeAsync();
        Assert.AreEqual(0, budget.RetainedBytes);
    }

    [TestMethod]
    public async Task UndoRedoAndRestoreAlwaysRenderFromOriginalFrame()
    {
        var renderer = new RecordingRenderer();
        var factory = CreateFactory(
            new ImageTranslationMemoryBudget(),
            new IncrementingTranslations(),
            renderer);
        var original = Frame();
        var session = factory.Create(original).Value;
        var recognition = new OcrRecognitionResult([Region("source")]);

        var edited = await session.TranslateAsync(recognition, [0], OcrLanguages.English);
        var undone = await session.UndoAsync();
        var redone = await session.RedoAsync();
        var restored = await session.RestoreOriginalAsync();

        Assert.IsTrue(edited.IsSuccess);
        Assert.IsTrue(undone.Value.IsOriginal);
        Assert.IsFalse(redone.Value.IsOriginal);
        Assert.IsTrue(restored.Value.IsOriginal);
        Assert.IsTrue(renderer.Backgrounds.All(background => ReferenceEquals(background, original)));
        await session.DisposeAsync();
    }

    [TestMethod]
    public async Task TranslateAsync_RendersEverySelectedRegionInOneEdit()
    {
        var renderer = new RecordingRenderer();
        var factory = CreateFactory(
            new ImageTranslationMemoryBudget(),
            new IncrementingTranslations(),
            renderer);
        var session = factory.Create(Frame()).Value;
        var recognition = new OcrRecognitionResult(
        [
            Region("first"),
            Region("second"),
            Region("third")
        ]);

        var result = await session.TranslateAsync(
            recognition,
            [0, 2],
            OcrLanguages.English);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(2, result.Value.ActiveOverlayCount);
        CollectionAssert.AreEquivalent(
            new[] { "first", "third" },
            renderer.LastOverlays.Select(overlay => overlay.Region.Text).ToArray());
        await session.DisposeAsync();
    }

    private static ImageTranslationEditSessionFactory CreateFactory(
        ImageTranslationMemoryBudget budget,
        IImageTranslationUseCases? translations = null,
        IImageTranslationRenderer? renderer = null)
    {
        var settings = SettingsTestData.CreateBundle();
        return new ImageTranslationEditSessionFactory(
            translations ?? new IncrementingTranslations(),
            renderer ?? new RecordingRenderer(),
            new FakeSettings(settings),
            new FakeLanguages(),
            budget);
    }

    private static ImageFrame Frame() => new(2, 2, 8, 96, 96, new byte[16]);

    private static OcrTextRegion Region(string text) => new(
        text,
        [new ImagePoint(0, 0), new ImagePoint(2, 0), new ImagePoint(2, 2), new ImagePoint(0, 2)],
        0);

    private sealed class IncrementingTranslations : IImageTranslationUseCases
    {
        private int _value;

        public Task<ImageTranslationResult> TranslateAsync(
            ImageTranslationRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ImageRegionTranslationResult> TranslateRegionsAsync(
            ImageRegionTranslationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImageRegionTranslationResult(
                request.RegionIndexes
                    .Select(index => new ImageRegionTranslation(index, $"translated-{Interlocked.Increment(ref _value)}"))
                    .ToArray(),
                []));
    }

    private sealed class RecordingRenderer : IImageTranslationRenderer
    {
        public List<ImageFrame> Backgrounds { get; } = [];
        public IReadOnlyList<ImageTranslationOverlay> LastOverlays { get; private set; } = [];

        public Task<ImageTranslationRenderResult> RenderAsync(
            ImageFrame background,
            IReadOnlyList<ImageTranslationOverlay> overlays,
            CancellationToken cancellationToken = default)
        {
            Backgrounds.Add(background);
            LastOverlays = overlays;
            return Task.FromResult(new ImageTranslationRenderResult(background, [], overlays.Count));
        }
    }

    private sealed class FakeLanguages : ITranslationLanguageCatalog
    {
        public IReadOnlyList<TranslationLanguage> All { get; } =
            [new("en", "English"), new("zh-Hans", "Chinese")];

        public TranslationLanguage Get(string id) =>
            All.FirstOrDefault(language => language.Id == id)
            ?? new TranslationLanguage(id, id);
    }

    private sealed class FakeSettings(SettingsBundle current) : ISettingsUseCases
    {
        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged { add { } remove { } }
        public event EventHandler<SettingsSaveFailedEventArgs>? SaveFailed { add { } remove { } }
        public bool IsInitialized => true;
        public SettingsBundle Current { get; } = current;
        public ValueTask<Result<SettingsBundle>> InitializeAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<SettingsBundle>.Success(Current));
        public Result Update(SettingsSection section, SettingsBundle settings) => Result.Success();
        public ValueTask<Result> FlushAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
