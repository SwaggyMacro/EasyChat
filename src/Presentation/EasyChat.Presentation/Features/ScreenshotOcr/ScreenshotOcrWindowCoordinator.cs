using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EasyChat.Contracts.Capture;
using EasyChat.Contracts.ImageTranslation;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.TextAssist;
using EasyChat.Presentation.Features.Capture;
using EasyChat.Presentation.Features.ScreenshotOcr.Views;
using EasyChat.Presentation.Features.TextAssist;
using EasyChat.Presentation.Features.Translation;
using EasyChat.Presentation.Foundation.Localization;
using EasyChat.Presentation.Foundation.Navigation;
using EasyChat.Presentation.ImageTranslation;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using EasyChat.Shared.Results;

namespace EasyChat.Presentation.Features.ScreenshotOcr;

public sealed record ScreenshotOcrLanguageOption(
    OcrLanguage Language,
    string DisplayName,
    string Icon,
    IReadOnlyList<string> ModelPackageIds)
{
    public string Id => Language.Id;
}

public sealed class ScreenshotOcrWindowCoordinator(
    IScreenshotUseCases screenshots,
    IOcrModelUseCases ocrModels,
    IImageTranslationEditSessionFactory editSessions,
    IClipboardText clipboardText,
    IClipboardImage clipboardImage,
    ITextAssistWindowCoordinator textAssist,
    ITranslationWindowCoordinator translation,
    ScreenshotCaptureCoordinator capture,
    TranslationLanguageOptions translationLanguages,
    ISukiToastManager toasts,
    ILoggerFactory loggerFactory)
{
    private readonly HashSet<ScreenshotOcrWindowView> _windows = [];

    public async ValueTask OpenAsync(
        ImageFrame image,
        PhysicalScreenPoint? anchor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var sessionResult = editSessions.Create(image);
        if (sessionResult.IsFailure)
        {
            await ShowErrorAsync(sessionResult.Error.Message, cancellationToken);
            return;
        }

        var session = sessionResult.Value;
        try
        {
            await OnUiAsync(() =>
            {
                Bitmap? bitmap = null;
                try
                {
                    bitmap = AvaloniaImageFrames.ToBitmap(image);
                    var viewModel = new ScreenshotOcrWindowViewModel(
                        screenshots,
                        ocrModels,
                        editSessions,
                        clipboardText,
                        clipboardImage,
                        textAssist,
                        translation,
                        capture,
                        translationLanguages,
                        session,
                        bitmap,
                        loggerFactory.CreateLogger<ScreenshotOcrWindowViewModel>());
                    var view = new ScreenshotOcrWindowView(viewModel);
                    if (anchor is { } point)
                        view.PositionNear(point);
                    view.Closed += (_, _) => _windows.Remove(view);
                    _windows.Add(view);
                    view.Show();
                    bitmap = null;
                    session = null!;
                }
                finally
                {
                    bitmap?.Dispose();
                }
            }, cancellationToken);
        }
        finally
        {
            if (session is not null)
                await session.DisposeAsync();
        }
    }

    private ValueTask ShowErrorAsync(string message, CancellationToken cancellationToken) =>
        OnUiAsync(() => toasts.CreateSimpleInfoToast()
            .WithTitle("Screenshot OCR")
            .WithContent(message)
            .Queue(), cancellationToken);

    private static async ValueTask OnUiAsync(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }
        await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken);
    }
}

public sealed class ScreenshotOcrWindowViewModel : ViewModelBase, IAsyncDisposable
{
    internal const int MaximumRegionCount = 20_000;
    internal const long MaximumTextBytes = 8L * 1024 * 1024;

    private readonly IScreenshotUseCases _screenshots;
    private readonly IOcrModelUseCases _ocrModels;
    private readonly IImageTranslationEditSessionFactory _editSessions;
    private readonly IClipboardText _clipboardText;
    private readonly IClipboardImage _clipboardImage;
    private readonly ITextAssistWindowCoordinator _textAssist;
    private readonly ITranslationWindowCoordinator _translation;
    private readonly ScreenshotCaptureCoordinator _capture;
    private readonly ILogger<ScreenshotOcrWindowViewModel> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private IImageTranslationEditSession _editSession;
    private Bitmap _bitmap;
    private OcrRecognitionResult _recognition = new([]);
    private CancellationTokenSource? _ocrRequest;
    private CancellationTokenSource? _translationRequest;
    private CancellationTokenSource? _editRequest;
    private long _ocrGeneration;
    private long _translationGeneration;
    private long _editGeneration;
    private string _sourceText = string.Empty;
    private string _translatedText = string.Empty;
    private string _errorMessage = string.Empty;
    private string _statusText = string.Empty;
    private bool _isRecognizing;
    private bool _isTranslating;
    private bool _isEditingImage;
    private bool _isShowingTranslation;
    private bool _canUndo;
    private bool _canRedo;
    private double _zoomPercent = 100;
    private ScreenshotOcrLanguageOption _candidateLanguage;
    private OcrLanguage _activeLanguage;
    private IReadOnlyList<int> _selectedRegionIndexes = [];
    private bool _disposed;

    internal ScreenshotOcrWindowViewModel(
        IScreenshotUseCases screenshots,
        IOcrModelUseCases ocrModels,
        IImageTranslationEditSessionFactory editSessions,
        IClipboardText clipboardText,
        IClipboardImage clipboardImage,
        ITextAssistWindowCoordinator textAssist,
        ITranslationWindowCoordinator translation,
        ScreenshotCaptureCoordinator capture,
        TranslationLanguageOptions translationLanguages,
        IImageTranslationEditSession editSession,
        Bitmap bitmap,
        ILogger<ScreenshotOcrWindowViewModel> logger)
    {
        _screenshots = screenshots;
        _ocrModels = ocrModels;
        _editSessions = editSessions;
        _clipboardText = clipboardText;
        _clipboardImage = clipboardImage;
        _textAssist = textAssist;
        _translation = translation;
        _capture = capture;
        _editSession = editSession;
        _bitmap = bitmap;
        _logger = logger;
        Languages = CreateLanguageOptions(ocrModels, translationLanguages);
        _activeLanguage = screenshots.ResolveOcrLanguage();
        _candidateLanguage = Languages.FirstOrDefault(option =>
                                 string.Equals(option.Id, _activeLanguage.Id, StringComparison.OrdinalIgnoreCase))
                             ?? Languages.FirstOrDefault()
                             ?? new ScreenshotOcrLanguageOption(
                                 _activeLanguage,
                                 _activeLanguage.DisplayName,
                                 "unknown.png",
                                 []);
    }

    public IReadOnlyList<ScreenshotOcrLanguageOption> Languages { get; }
    public ISukiDialogManager DialogManager { get; } = new SukiDialogManager();
    public Bitmap Bitmap => _bitmap;
    public IReadOnlyList<OcrTextRegion> Regions => _recognition.Regions;
    public Func<string, Task<bool>>? ConfirmResetAsync { get; set; }
    public event Action<Bitmap, bool>? BitmapChanged;
    public event Action<IReadOnlyList<OcrTextRegion>>? RegionsChanged;

    public string SourceText
    {
        get => _sourceText;
        set
        {
            this.RaiseAndSetIfChanged(ref _sourceText, LimitText(value));
            this.RaisePropertyChanged(nameof(CurrentText));
        }
    }

    public string TranslatedText
    {
        get => _translatedText;
        set
        {
            this.RaiseAndSetIfChanged(ref _translatedText, LimitText(value));
            this.RaisePropertyChanged(nameof(CurrentText));
        }
    }

    public string CurrentText => IsShowingTranslation ? TranslatedText : SourceText;
    public string ErrorMessage { get => _errorMessage; private set => this.RaiseAndSetIfChanged(ref _errorMessage, value); }
    public string StatusText { get => _statusText; private set => this.RaiseAndSetIfChanged(ref _statusText, value); }
    public bool IsRecognizing { get => _isRecognizing; private set => this.RaiseAndSetIfChanged(ref _isRecognizing, value); }
    public bool IsTranslating { get => _isTranslating; private set => this.RaiseAndSetIfChanged(ref _isTranslating, value); }
    public bool IsEditingImage { get => _isEditingImage; private set => this.RaiseAndSetIfChanged(ref _isEditingImage, value); }
    public bool IsBusy => IsRecognizing || IsTranslating || IsEditingImage;
    public bool IsShowingTranslation
    {
        get => _isShowingTranslation;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isShowingTranslation, value);
            this.RaisePropertyChanged(nameof(CurrentText));
        }
    }
    public bool CanUndo { get => _canUndo; private set => this.RaiseAndSetIfChanged(ref _canUndo, value); }
    public bool CanRedo { get => _canRedo; private set => this.RaiseAndSetIfChanged(ref _canRedo, value); }
    public bool HasSelection => _selectedRegionIndexes.Count > 0;
    public double ZoomPercent
    {
        get => _zoomPercent;
        internal set
        {
            this.RaiseAndSetIfChanged(ref _zoomPercent, value);
            this.RaisePropertyChanged(nameof(ZoomText));
        }
    }
    public string ZoomText => $"{ZoomPercent:0}%";

    public ScreenshotOcrLanguageOption CandidateLanguage
    {
        get => _candidateLanguage;
        set
        {
            if (value is not null)
                this.RaiseAndSetIfChanged(ref _candidateLanguage, value);
        }
    }

    public async Task InitializeAsync()
    {
        if (_disposed)
            return;
        await RecognizeCurrentAsync(requireConfirmation: false);
    }

    public void SetSelectedRegions(IReadOnlyList<int> indexes)
    {
        _selectedRegionIndexes = indexes
            .Where(index => index >= 0 && index < _recognition.Regions.Count)
            .Distinct()
            .OrderBy(index => _recognition.Regions[index].Polygon.Min(point => point.Y))
            .ThenBy(index => _recognition.Regions[index].Polygon.Min(point => point.X))
            .ToArray();
        this.RaisePropertyChanged(nameof(HasSelection));
    }

    public Result ValidateImageDimensions(int width, int height) =>
        _editSessions.ValidateImage(width, height);

    public async Task RetryAsync() => await RecognizeCurrentAsync(requireConfirmation: true);

    public async Task ReplaceImageAsync(ImageFrame image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!await ConfirmDiscardAsync())
            return;

        var sessionResult = _editSessions.Create(image);
        if (sessionResult.IsFailure)
        {
            ErrorMessage = sessionResult.Error.Message;
            return;
        }

        var replacementSession = sessionResult.Value;
        Bitmap? replacementBitmap = null;
        CancelDependentOperations();
        var generation = Interlocked.Increment(ref _ocrGeneration);
        var request = ReplaceRequest(ref _ocrRequest);
        IsRecognizing = true;
        RaiseBusy();
        ErrorMessage = string.Empty;
        StatusText = "Recognizing text...";
        try
        {
            replacementBitmap = AvaloniaImageFrames.ToBitmap(image);
            var recognition = await _screenshots.RecognizeAsync(
                image,
                enableRotation: true,
                CandidateLanguage.Language,
                request.Token);
            ValidateRecognition(recognition, image);
            if (generation != _ocrGeneration || request.IsCancellationRequested || _disposed)
                return;

            var previousSession = _editSession;
            _editSession = replacementSession;
            replacementSession = null!;
            SetBitmap(replacementBitmap, resetView: true);
            replacementBitmap = null;
            CommitRecognition(recognition, CandidateLanguage.Language);
            await previousSession.DisposeAsync();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (OcrModelNotDownloadedException exception)
        {
            ErrorMessage = BuildMissingModelMessage(exception.Language);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to replace the screenshot OCR image.");
            ErrorMessage = exception.Message;
        }
        finally
        {
            replacementBitmap?.Dispose();
            if (replacementSession is not null)
                await replacementSession.DisposeAsync();
            if (CompleteRequest(ref _ocrRequest, request))
            {
                IsRecognizing = false;
                RaiseBusy();
            }
        }
    }

    public async Task RecaptureAsync()
    {
        if (IsBusy)
            return;
        try
        {
            var selection = await _capture.CaptureAsync(
                mode: null,
                CaptureOverlayAction.OcrWorkbench,
                CaptureToolbarMode.ImageSelection,
                _lifetime.Token);
            if (selection is not null)
                await ReplaceImageAsync(selection.Image);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    public async Task TranslateTextAsync()
    {
        if (IsRecognizing || string.IsNullOrWhiteSpace(SourceText))
            return;
        var generation = Interlocked.Increment(ref _translationGeneration);
        var request = ReplaceRequest(ref _translationRequest);
        IsTranslating = true;
        RaiseBusy();
        ErrorMessage = string.Empty;
        TranslatedText = string.Empty;
        IsShowingTranslation = true;
        try
        {
            await foreach (var item in _screenshots.TranslateTextAsync(
                               SourceText,
                               _activeLanguage,
                               request.Token))
            {
                if (generation != _translationGeneration || request.IsCancellationRequested || _disposed)
                    return;
                switch (item)
                {
                    case Contracts.Translation.TranslationDeltaEvent delta:
                        TranslatedText += delta.Text;
                        break;
                    case Contracts.Translation.TranslationFailedEvent failed:
                        throw new InvalidOperationException(failed.Error.Message);
                }
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Screenshot OCR text translation failed.");
            ErrorMessage = exception.Message;
        }
        finally
        {
            if (CompleteRequest(ref _translationRequest, request))
            {
                IsTranslating = false;
                RaiseBusy();
            }
        }
    }

    public void ShowOriginal() => IsShowingTranslation = false;

    public async Task CopyCurrentTextAsync() => await WriteTextAsync(CurrentText);

    public async Task CopySelectedTextAsync() => await WriteTextAsync(GetSelectedText());

    public async Task CopyImageAsync()
    {
        try
        {
            var frame = AvaloniaImageFrames.ToImageFrame(_bitmap);
            var result = await _clipboardImage.WriteAsync(frame, _lifetime.Token);
            if (result.IsFailure)
                ErrorMessage = result.Error.Message;
            else
                StatusText = "Image copied.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = exception.Message;
        }
    }

    public async Task ShowSelectedTranslationAsync(PhysicalScreenPoint anchor)
    {
        var text = GetSelectedText();
        if (!string.IsNullOrWhiteSpace(text))
        {
            await _translation.ShowSentenceAsync(
                text,
                anchor,
                cancellationToken: _lifetime.Token);
        }
    }

    public async Task ExplainSelectedAsync(PhysicalScreenPoint anchor)
    {
        var text = GetSelectedText();
        if (!string.IsNullOrWhiteSpace(text))
            await _textAssist.ShowResultAsync(text, TextAssistOperation.Explanation, anchor, _lifetime.Token);
    }

    public async Task ReplaceSelectedWithTranslationAsync()
    {
        if (IsRecognizing || _selectedRegionIndexes.Count == 0)
            return;
        var generation = Interlocked.Increment(ref _editGeneration);
        var session = _editSession;
        var request = ReplaceRequest(ref _editRequest);
        IsEditingImage = true;
        RaiseBusy();
        ErrorMessage = string.Empty;
        try
        {
            var result = await session.TranslateAsync(
                _recognition,
                _selectedRegionIndexes,
                _activeLanguage,
                request.Token);
            if (generation == _editGeneration
                && ReferenceEquals(session, _editSession)
                && !request.IsCancellationRequested
                && !_disposed)
            {
                ApplyEditResult(result);
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        finally
        {
            if (CompleteRequest(ref _editRequest, request))
            {
                IsEditingImage = false;
                RaiseBusy();
            }
        }
    }

    public Task UndoAsync() => ApplyHistoryAsync(_editSession.UndoAsync);
    public Task RedoAsync() => ApplyHistoryAsync(_editSession.RedoAsync);
    public Task RestoreOriginalAsync() => ApplyHistoryAsync(_editSession.RestoreOriginalAsync);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetime.Cancel();
        _ocrRequest?.Cancel();
        _translationRequest?.Cancel();
        _editRequest?.Cancel();
        _ocrRequest?.Dispose();
        _translationRequest?.Dispose();
        _editRequest?.Dispose();
        _bitmap.Dispose();
        await _editSession.DisposeAsync();
        _lifetime.Dispose();
    }

    private async Task RecognizeCurrentAsync(bool requireConfirmation)
    {
        if (requireConfirmation && !await ConfirmDiscardAsync())
            return;

        CancelDependentOperations();
        var generation = Interlocked.Increment(ref _ocrGeneration);
        var request = ReplaceRequest(ref _ocrRequest);
        IsRecognizing = true;
        RaiseBusy();
        ErrorMessage = string.Empty;
        StatusText = "Recognizing text...";
        Bitmap? restored = null;
        try
        {
            var recognition = await _screenshots.RecognizeAsync(
                _editSession.OriginalImage,
                enableRotation: true,
                CandidateLanguage.Language,
                request.Token);
            ValidateRecognition(recognition, _editSession.OriginalImage);
            if (generation != _ocrGeneration || request.IsCancellationRequested || _disposed)
                return;

            if (requireConfirmation && _editSession.HasChanges)
            {
                restored = AvaloniaImageFrames.ToBitmap(_editSession.OriginalImage);
                await _editSession.ResetHistoryAsync(request.Token);
                SetBitmap(restored, resetView: true);
                restored = null;
            }
            CommitRecognition(recognition, CandidateLanguage.Language);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (OcrModelNotDownloadedException exception)
        {
            ErrorMessage = BuildMissingModelMessage(exception.Language);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Screenshot OCR recognition failed.");
            ErrorMessage = exception.Message;
        }
        finally
        {
            restored?.Dispose();
            if (CompleteRequest(ref _ocrRequest, request))
            {
                IsRecognizing = false;
                RaiseBusy();
            }
        }
    }

    private async Task ApplyHistoryAsync(
        Func<CancellationToken, Task<Result<ImageTranslationEditResult>>> operation)
    {
        if (IsRecognizing)
            return;
        var generation = Interlocked.Increment(ref _editGeneration);
        var session = _editSession;
        var request = ReplaceRequest(ref _editRequest);
        IsEditingImage = true;
        RaiseBusy();
        try
        {
            var result = await operation(request.Token);
            if (generation == _editGeneration
                && ReferenceEquals(session, _editSession)
                && !request.IsCancellationRequested
                && !_disposed)
            {
                ApplyEditResult(result);
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        finally
        {
            if (CompleteRequest(ref _editRequest, request))
            {
                IsEditingImage = false;
                RaiseBusy();
            }
        }
    }

    private void ApplyEditResult(Result<ImageTranslationEditResult> result)
    {
        if (result.IsFailure)
        {
            ErrorMessage = result.Error.Message;
            return;
        }
        var bitmap = AvaloniaImageFrames.ToBitmap(result.Value.Image);
        SetBitmap(bitmap, resetView: false);
        CanUndo = result.Value.CanUndo;
        CanRedo = result.Value.CanRedo;
        StatusText = result.Value.Warnings.FirstOrDefault() ?? string.Empty;
    }

    private void CommitRecognition(OcrRecognitionResult recognition, OcrLanguage language)
    {
        _recognition = recognition;
        _activeLanguage = language;
        SourceText = recognition.Text;
        TranslatedText = string.Empty;
        IsShowingTranslation = false;
        SetSelectedRegions([]);
        CanUndo = _editSession.CanUndo;
        CanRedo = _editSession.CanRedo;
        StatusText = recognition.Regions.Count == 0
            ? "No text detected."
            : $"Recognized {recognition.Regions.Count:N0} text regions.";
        RegionsChanged?.Invoke(recognition.Regions);
    }

    private void SetBitmap(Bitmap bitmap, bool resetView)
    {
        var previous = _bitmap;
        _bitmap = bitmap;
        BitmapChanged?.Invoke(bitmap, resetView);
        previous.Dispose();
    }

    private async Task WriteTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        var result = await _clipboardText.WriteAsync(text, _lifetime.Token);
        if (result.IsFailure)
            ErrorMessage = result.Error.Message;
        else
            StatusText = "Text copied.";
    }

    private string GetSelectedText() => string.Join(
        Environment.NewLine,
        _selectedRegionIndexes.Select(index => _recognition.Regions[index].Text));

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_editSession.HasChanges
            && string.Equals(SourceText, _recognition.Text, StringComparison.Ordinal)
            && string.IsNullOrEmpty(TranslatedText))
        {
            return true;
        }
        return ConfirmResetAsync is null
               || await ConfirmResetAsync("Current OCR edits and translation will be replaced. Continue?");
    }

    private string BuildMissingModelMessage(OcrLanguage language)
    {
        var packages = _ocrModels.ModelPackages
            .Where(package => package.SupportedLanguages.Any(candidate =>
                string.Equals(candidate.Id, language.Id, StringComparison.OrdinalIgnoreCase)))
            .Select(package => package.Id)
            .ToArray();
        var packageText = packages.Length == 0 ? language.Id : string.Join(", ", packages);
        return $"OCR model '{packageText}' is not installed. Open Settings > OCR Models.";
    }

    private static void ValidateRecognition(
        OcrRecognitionResult recognition,
        ImageFrame image)
    {
        ArgumentNullException.ThrowIfNull(recognition);
        ArgumentNullException.ThrowIfNull(image);
        if (recognition.Regions.Count > MaximumRegionCount)
            throw new InvalidDataException($"OCR returned more than {MaximumRegionCount:N0} text regions.");
        long textBytes = 0;
        for (var index = 0; index < recognition.Regions.Count; index++)
        {
            var region = recognition.Regions[index];
            textBytes = checked(textBytes
                                + (long)(region.Text?.Length ?? 0) * sizeof(char)
                                + (index == 0 ? 0 : sizeof(char)));
            if (textBytes > MaximumTextBytes)
                throw new InvalidDataException("OCR text exceeds the 8 MiB workspace limit.");
            if (region.Polygon.Count is < 3 or > 64 || region.Polygon.Any(point =>
                    !double.IsFinite(point.X)
                    || !double.IsFinite(point.Y)
                    || point.X < -1
                    || point.Y < -1
                    || point.X > image.Width + 1
                    || point.Y > image.Height + 1))
            {
                throw new InvalidDataException("OCR returned an invalid text polygon.");
            }
        }
    }

    private static IReadOnlyList<ScreenshotOcrLanguageOption> CreateLanguageOptions(
        IOcrModelUseCases models,
        TranslationLanguageOptions translations) =>
        models.ModelPackages
            .SelectMany(package => package.SupportedLanguages.Select(language => (package.Id, Language: language)))
            .GroupBy(item => item.Language.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var language = group.First().Language;
                var translated = translations.All.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, language.Id, StringComparison.OrdinalIgnoreCase));
                var display = translated is null
                    ? language.DisplayName
                    : LanguageDisplayNames.ForUi(translated.ChineseName, translated.EnglishName);
                return new ScreenshotOcrLanguageOption(
                    language,
                    display,
                    translated?.Icon ?? "unknown.png",
                    group.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            })
            .OrderBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    private static string LimitText(string? value)
    {
        var text = value ?? string.Empty;
        var maximumChars = checked((int)(MaximumTextBytes / sizeof(char)));
        return text.Length <= maximumChars ? text : text[..maximumChars];
    }

    private CancellationTokenSource ReplaceRequest(ref CancellationTokenSource? target)
    {
        target?.Cancel();
        target?.Dispose();
        target = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        return target;
    }

    private static bool CompleteRequest(
        ref CancellationTokenSource? target,
        CancellationTokenSource completed)
    {
        if (!ReferenceEquals(target, completed))
            return false;
        target = null;
        completed.Dispose();
        return true;
    }

    private void CancelDependentOperations()
    {
        Interlocked.Increment(ref _translationGeneration);
        Interlocked.Increment(ref _editGeneration);
        _translationRequest?.Cancel();
        _editRequest?.Cancel();
    }

    private void RaiseBusy() => this.RaisePropertyChanged(nameof(IsBusy));
}
