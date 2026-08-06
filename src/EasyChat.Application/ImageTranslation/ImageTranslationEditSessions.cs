using EasyChat.Contracts.ImageTranslation;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Translation;
using EasyChat.Shared.Results;

namespace EasyChat.Application.ImageTranslation;

public sealed class ImageTranslationMemoryBudget
{
    public const long MaximumImageBytes = 128L * 1024 * 1024;
    public const long MaximumRetainedBytes = 512L * 1024 * 1024;

    private long _retainedBytes;

    public long RetainedBytes => Interlocked.Read(ref _retainedBytes);

    internal bool TryReserve(long bytes)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _retainedBytes);
            if (bytes < 0 || current > MaximumRetainedBytes - bytes)
                return false;
            if (Interlocked.CompareExchange(ref _retainedBytes, current + bytes, current) == current)
                return true;
        }
    }

    internal void Release(long bytes)
    {
        var remaining = Interlocked.Add(ref _retainedBytes, -bytes);
        if (remaining < 0)
            throw new InvalidOperationException("The image translation memory budget was released more than once.");
    }
}

public sealed class ImageTranslationEditSessionFactory(
    IImageTranslationUseCases translations,
    IImageTranslationRenderer renderer,
    ISettingsUseCases settings,
    ITranslationLanguageCatalog languages,
    ImageTranslationMemoryBudget budget) : IImageTranslationEditSessionFactory
{
    public Result ValidateImage(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return Result.Failure(new Error("image-translation.image-invalid", "The image dimensions are invalid."));
        long imageBytes;
        try
        {
            imageBytes = checked((long)width * height * 4);
        }
        catch (OverflowException)
        {
            return Result.Failure(new Error("image-translation.image-too-large", "The image dimensions are too large."));
        }

        if (imageBytes > ImageTranslationMemoryBudget.MaximumImageBytes)
        {
            return Result.Failure(new Error(
                "image-translation.image-too-large",
                "The decoded image exceeds the 128 MiB OCR workspace limit."));
        }

        return budget.RetainedBytes > ImageTranslationMemoryBudget.MaximumRetainedBytes - imageBytes * 2
            ? Result.Failure(new Error(
                "image-translation.memory-budget-exceeded",
                "The OCR workspace image memory budget is full. Close another OCR window and retry."))
            : Result.Success();
    }

    public Result<IImageTranslationEditSession> Create(ImageFrame originalImage)
    {
        ArgumentNullException.ThrowIfNull(originalImage);
        var imageBytes = checked((long)originalImage.Stride * originalImage.Height);
        var validation = ValidateImage(originalImage.Width, originalImage.Height);
        if (validation.IsFailure)
            return Result<IImageTranslationEditSession>.Failure(validation.Error);
        if (imageBytes > ImageTranslationMemoryBudget.MaximumImageBytes)
        {
            return Result<IImageTranslationEditSession>.Failure(new Error(
                "image-translation.image-too-large",
                "The decoded image exceeds the 128 MiB OCR workspace limit."));
        }

        var retainedBytes = checked(imageBytes * 2);
        if (!budget.TryReserve(retainedBytes))
        {
            return Result<IImageTranslationEditSession>.Failure(new Error(
                "image-translation.memory-budget-exceeded",
                "The OCR workspace image memory budget is full. Close another OCR window and retry."));
        }

        return Result<IImageTranslationEditSession>.Success(
            new ImageTranslationEditSession(
                originalImage,
                retainedBytes,
                translations,
                renderer,
                settings,
                languages,
                budget));
    }
}

internal sealed class ImageTranslationEditSession : IImageTranslationEditSession
{
    private const int MaximumHistoryCount = 100;
    private const long MaximumHistoryTextBytes = 2L * 1024 * 1024;

    private readonly long _retainedBytes;
    private readonly IImageTranslationUseCases _translations;
    private readonly IImageTranslationRenderer _renderer;
    private readonly ISettingsUseCases _settings;
    private readonly ITranslationLanguageCatalog _languages;
    private readonly ImageTranslationMemoryBudget _budget;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly Dictionary<int, string> _active = [];
    private readonly Dictionary<int, OcrTextRegion> _regions = [];
    private readonly LinkedList<EditDelta> _undo = [];
    private readonly LinkedList<EditDelta> _redo = [];
    private long _historyTextBytes;
    private bool _disposed;

    public ImageTranslationEditSession(
        ImageFrame originalImage,
        long retainedBytes,
        IImageTranslationUseCases translations,
        IImageTranslationRenderer renderer,
        ISettingsUseCases settings,
        ITranslationLanguageCatalog languages,
        ImageTranslationMemoryBudget budget)
    {
        OriginalImage = originalImage;
        _retainedBytes = retainedBytes;
        _translations = translations;
        _renderer = renderer;
        _settings = settings;
        _languages = languages;
        _budget = budget;
    }

    public ImageFrame OriginalImage { get; }
    public bool CanUndo { get { lock (_stateGate) return _undo.Count > 0; } }
    public bool CanRedo { get { lock (_stateGate) return _redo.Count > 0; } }
    public bool HasChanges { get { lock (_stateGate) return _active.Count > 0; } }

    public async Task<Result<ImageTranslationEditResult>> TranslateAsync(
        OcrRecognitionResult recognition,
        IReadOnlyList<int> regionIndexes,
        OcrLanguage sourceLanguage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recognition);
        ArgumentNullException.ThrowIfNull(regionIndexes);
        ArgumentNullException.ThrowIfNull(sourceLanguage);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var source = ResolveLanguage(sourceLanguage.Id);
            var target = _languages.Get(_settings.Current.General.TargetLanguage.Id);
            var translated = await _translations.TranslateRegionsAsync(
                new ImageRegionTranslationRequest(recognition, regionIndexes, source, target),
                cancellationToken).ConfigureAwait(false);
            if (translated.Translations.Count == 0)
            {
                return Result<ImageTranslationEditResult>.Failure(new Error(
                    "image-translation.no-regions-translated",
                    translated.Warnings.FirstOrDefault() ?? "No selected text could be translated."));
            }

            Dictionary<int, string> current;
            Dictionary<int, OcrTextRegion> regions;
            lock (_stateGate)
            {
                current = new Dictionary<int, string>(_active);
                regions = new Dictionary<int, OcrTextRegion>(_regions);
            }

            var before = new Dictionary<int, string?>();
            var after = new Dictionary<int, string?>();
            foreach (var item in translated.Translations)
            {
                var previous = current.GetValueOrDefault(item.RegionIndex);
                if (string.Equals(previous, item.Translation, StringComparison.Ordinal))
                    continue;
                before[item.RegionIndex] = previous;
                after[item.RegionIndex] = item.Translation;
                current[item.RegionIndex] = item.Translation;
                regions[item.RegionIndex] = recognition.Regions[item.RegionIndex];
            }

            if (after.Count == 0)
                return await RenderResultAsync(current, regions, translated.Warnings, cancellationToken)
                    .ConfigureAwait(false);

            var rendered = await RenderResultAsync(current, regions, translated.Warnings, cancellationToken)
                .ConfigureAwait(false);
            if (rendered.IsFailure)
                return rendered;

            lock (_stateGate)
            {
                Replace(_active, current);
                Replace(_regions, regions);
                ClearHistory(_redo);
                PushUndo(new EditDelta(before, after));
            }
            return Result<ImageTranslationEditResult>.Success(rendered.Value with
            {
                CanUndo = true,
                CanRedo = false
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result<ImageTranslationEditResult>.Failure(new Error(
                "image-translation.edit-failed",
                exception.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<Result<ImageTranslationEditResult>> UndoAsync(
        CancellationToken cancellationToken = default) =>
        MoveHistoryAsync(undo: true, cancellationToken);

    public Task<Result<ImageTranslationEditResult>> RedoAsync(
        CancellationToken cancellationToken = default) =>
        MoveHistoryAsync(undo: false, cancellationToken);

    public async Task<Result<ImageTranslationEditResult>> RestoreOriginalAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            Dictionary<int, string?> before;
            lock (_stateGate)
            {
                if (_active.Count == 0)
                    return CreateOriginalResult();
                before = _active.ToDictionary(item => item.Key, item => (string?)item.Value);
                _active.Clear();
                ClearHistory(_redo);
                PushUndo(new EditDelta(before, before.Keys.ToDictionary(key => key, _ => (string?)null)));
                return CreateOriginalResult();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ResetHistoryAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            lock (_stateGate)
            {
                _active.Clear();
                _regions.Clear();
                ClearHistory(_undo);
                ClearHistory(_redo);
                _historyTextBytes = 0;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            lock (_stateGate)
            {
                _active.Clear();
                _regions.Clear();
                _undo.Clear();
                _redo.Clear();
                _historyTextBytes = 0;
            }
            _budget.Release(_retainedBytes);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task<Result<ImageTranslationEditResult>> MoveHistoryAsync(
        bool undo,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EditDelta? delta;
            Dictionary<int, string> current;
            Dictionary<int, OcrTextRegion> regions;
            lock (_stateGate)
            {
                var source = undo ? _undo : _redo;
                delta = source.Last?.Value;
                if (delta is null)
                    return CurrentWithoutRendering();
                current = new Dictionary<int, string>(_active);
                regions = new Dictionary<int, OcrTextRegion>(_regions);
            }

            Apply(current, undo ? delta.Before : delta.After);
            var rendered = await RenderResultAsync(current, regions, [], cancellationToken)
                .ConfigureAwait(false);
            if (rendered.IsFailure)
                return rendered;

            lock (_stateGate)
            {
                var source = undo ? _undo : _redo;
                var target = undo ? _redo : _undo;
                source.RemoveLast();
                target.AddLast(delta);
                Replace(_active, current);
                var value = rendered.Value with
                {
                    CanUndo = _undo.Count > 0,
                    CanRedo = _redo.Count > 0
                };
                return Result<ImageTranslationEditResult>.Success(value);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result<ImageTranslationEditResult>.Failure(new Error(
                "image-translation.history-failed",
                exception.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Result<ImageTranslationEditResult>> RenderResultAsync(
        IReadOnlyDictionary<int, string> active,
        IReadOnlyDictionary<int, OcrTextRegion> regions,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        if (active.Count == 0)
            return CreateOriginalResult(warnings);
        var overlays = active
            .OrderBy(item => item.Key)
            .Select(item => new ImageTranslationOverlay(regions[item.Key], item.Value))
            .ToArray();
        var rendered = await _renderer.RenderAsync(OriginalImage, overlays, cancellationToken)
            .ConfigureAwait(false);
        return Result<ImageTranslationEditResult>.Success(new ImageTranslationEditResult(
            rendered.Image,
            [.. warnings, .. rendered.Warnings],
            false,
            CanUndo,
            CanRedo,
            active.Count));
    }

    private Result<ImageTranslationEditResult> CurrentWithoutRendering()
    {
        lock (_stateGate)
        {
            return _active.Count == 0
                ? CreateOriginalResult()
                : Result<ImageTranslationEditResult>.Failure(new Error(
                    "image-translation.history-empty",
                    "There is no image edit history in that direction."));
        }
    }

    private Result<ImageTranslationEditResult> CreateOriginalResult(
        IReadOnlyList<string>? warnings = null) =>
        Result<ImageTranslationEditResult>.Success(new ImageTranslationEditResult(
            OriginalImage,
            warnings ?? [],
            true,
            CanUndo,
            CanRedo,
            0));

    private TranslationLanguage? ResolveLanguage(string languageId)
    {
        try
        {
            return _languages.Get(languageId);
        }
        catch
        {
            return null;
        }
    }

    private void PushUndo(EditDelta delta)
    {
        _undo.AddLast(delta);
        _historyTextBytes += delta.TextBytes;
        while (_undo.Count > MaximumHistoryCount || _historyTextBytes > MaximumHistoryTextBytes)
        {
            var oldest = _undo.First;
            if (oldest is null)
                break;
            _historyTextBytes -= oldest.Value.TextBytes;
            _undo.RemoveFirst();
        }
    }

    private void ClearHistory(LinkedList<EditDelta> history)
    {
        foreach (var delta in history)
            _historyTextBytes -= delta.TextBytes;
        history.Clear();
    }

    private static void Apply(
        IDictionary<int, string> target,
        IReadOnlyDictionary<int, string?> values)
    {
        foreach (var item in values)
        {
            if (item.Value is null)
                target.Remove(item.Key);
            else
                target[item.Key] = item.Value;
        }
    }

    private static void Replace<TKey, TValue>(
        IDictionary<TKey, TValue> target,
        IReadOnlyDictionary<TKey, TValue> source)
        where TKey : notnull
    {
        target.Clear();
        foreach (var item in source)
            target[item.Key] = item.Value;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record EditDelta(
        IReadOnlyDictionary<int, string?> Before,
        IReadOnlyDictionary<int, string?> After)
    {
        public long TextBytes { get; } = Before.Values.Concat(After.Values)
            .Where(value => value is not null)
            .Sum(value => checked((long)value!.Length * sizeof(char)));
    }
}
