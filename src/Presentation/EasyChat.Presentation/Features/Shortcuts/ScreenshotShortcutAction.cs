using System.Text;
using EasyChat.Contracts.Capture;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Shortcuts;
using EasyChat.Contracts.Speech;
using EasyChat.Contracts.Translation;
using EasyChat.Presentation.Features.Capture;
using EasyChat.Presentation.Features.ScreenshotOcr;
using EasyChat.Presentation.Features.Settings.State;
using Microsoft.Extensions.Logging;

namespace EasyChat.Presentation.Features.Shortcuts;

public sealed class ScreenshotShortcutAction(
    ScreenshotCaptureCoordinator capture,
    ScreenshotResultCoordinator results,
    IScreenshotUseCases screenshots,
    ITtsUseCases tts,
    ScreenshotOcrWindowCoordinator ocrWorkbench,
    SettingsSession settings,
    ILogger<ScreenshotShortcutAction> logger) : IShortcutAction
{
    private readonly ScreenshotCaptureCoordinator _capture = capture;
    private readonly ScreenshotResultCoordinator _results = results;
    private readonly IScreenshotUseCases _screenshots = screenshots;
    private readonly ITtsUseCases _tts = tts;
    private readonly ScreenshotOcrWindowCoordinator _ocrWorkbench = ocrWorkbench;
    private readonly SettingsSession _settings = settings;
    private readonly ILogger<ScreenshotShortcutAction> _logger = logger;
    private CancellationTokenSource? _imageTranslationCancellation;

    public string ActionType => "Screenshot";
    public bool PreventConcurrentExecution => true;

    public async ValueTask ExecuteAsync(
        ShortcutParameterSettings? parameter,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var selection = await _capture.CaptureAsync(
                _settings.Screenshot.Mode,
                cancellationToken: cancellationToken);
            if (selection is null)
                return;

            _ = ProcessAsync(selection.Image, selection.Action, selection.CompletionPoint);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to start screenshot translation.");
            await _results.ShowMessageAsync("Screenshot Error", exception.Message, cancellationToken);
        }
    }

    internal async Task ProcessAsync(
        ImageFrame image,
        CaptureOverlayAction action,
        PhysicalScreenPoint completionPoint)
    {
        CancellationTokenSource? imageCancellation = null;
        try
        {
            if (action == CaptureOverlayAction.OcrWorkbench)
            {
                await _ocrWorkbench.OpenAsync(image, completionPoint);
                return;
            }

            if (action == CaptureOverlayAction.CopyImageTranslated)
            {
                imageCancellation = new CancellationTokenSource();
                var previous = Interlocked.Exchange(
                    ref _imageTranslationCancellation,
                    imageCancellation);
                previous?.Cancel();
                previous?.Dispose();
            }

            var cancellationToken = imageCancellation?.Token ?? CancellationToken.None;
            var recognition = await _screenshots.RecognizeAsync(
                image,
                enableRotation: action == CaptureOverlayAction.CopyImageTranslated,
                cancellationToken: cancellationToken);
            if (action == CaptureOverlayAction.CopyImageTranslated)
            {
                await ProcessImageAsync(image, recognition, completionPoint, cancellationToken);
                return;
            }

            var text = recognition.Text;
            image = null!;
            recognition = null!;
            await ProcessTextAsync(text, action, completionPoint);
        }
        catch (OperationCanceledException) when (imageCancellation?.IsCancellationRequested == true)
        {
        }
        catch (OcrModelNotDownloadedException)
        {
            await _results.ShowMessageAsync(
                EasyChat.Presentation.Lang.Resources.OcrModelRequiredTitle,
                EasyChat.Presentation.Lang.Resources.OcrModelRequiredMessage);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Screenshot OCR processing failed.");
            await _results.ShowMessageAsync("OCR Error", exception.Message);
        }
        finally
        {
            if (imageCancellation is not null && ReferenceEquals(
                    Interlocked.CompareExchange(
                        ref _imageTranslationCancellation,
                        null,
                        imageCancellation),
                    imageCancellation))
            {
                imageCancellation.Dispose();
            }
        }
    }

    private async Task ProcessTextAsync(
        string text,
        CaptureOverlayAction action,
        PhysicalScreenPoint completionPoint)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            await _results.ShowMessageAsync("OCR Warning", "No text detected.");
            return;
        }

        if (action == CaptureOverlayAction.CopyOriginal)
            await _results.CopyTextAsync(text);

        if (_settings.Result.ScreenshotResultMode == ResultWindowMode.Dictionary)
        {
            await _results.ShowDictionaryAsync(text, completionPoint);
            return;
        }

        var window = await _results.OpenClassicAsync(completionPoint);
        var translation = new StringBuilder();
        try
        {
            await foreach (var item in _screenshots.TranslateTextAsync(text))
            {
                switch (item)
                {
                    case TranslationDeltaEvent delta when !string.IsNullOrEmpty(delta.Text):
                        translation.Append(delta.Text);
                        if (!window.IsClosed)
                            window.Append(delta.Text);
                        break;
                    case TranslationFailedEvent failed:
                        throw new InvalidOperationException(failed.Error.Message);
                }
            }

            if (!window.IsClosed && action is CaptureOverlayAction.CopyTranslated
                or CaptureOverlayAction.CopyBilingual)
            {
                var copied = action == CaptureOverlayAction.CopyBilingual
                    ? $"{text}\n\n{translation}"
                    : translation.ToString();
                await _results.CopyTextAsync(copied);
            }

            if (!window.IsClosed && translation.Length > 0)
                _ = ReadAloudAsync(text, translation.ToString());

            if (!window.IsClosed)
            {
                var delay = _settings.Result.EnableAutoReadDelay
                    ? Math.Max(2000, translation.Length * _settings.Result.MsPerChar)
                    : _settings.Result.AutoCloseDelay;
                window.CloseAfterDelay(delay);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Screenshot translation failed.");
            if (!window.IsClosed)
            {
                window.Close();
                var message = exception.InnerException is null
                    ? exception.Message
                    : $"{exception.Message} -> {exception.InnerException.Message}";
                await _results.ShowMessageAsync("Translation Error", message);
            }
        }
    }

    private async Task ProcessImageAsync(
        ImageFrame image,
        OcrRecognitionResult recognition,
        PhysicalScreenPoint completionPoint,
        CancellationToken cancellationToken)
    {
        if (recognition.Regions.Count == 0)
        {
            await _results.ShowMessageAsync("OCR Warning", "No text detected.");
            return;
        }

        var result = await _screenshots.TranslateImageAsync(
            image,
            recognition,
            cancellationToken);
        if (result.TranslatedBlockCount == 0)
        {
            await _results.ShowMessageAsync(
                "Image Translation",
                result.Warnings.FirstOrDefault() ?? "No text could be translated.");
            return;
        }

        await _results.ShowImageAsync(
            result.Image,
            result.Warnings,
            completionPoint,
            cancellationToken);
    }

    private async Task ReadAloudAsync(string sourceText, string targetText)
    {
        try
        {
            var readMode = _settings.Result.ReadAloudMode;
            if (readMode == ResultReadAloudMode.None || string.IsNullOrWhiteSpace(_settings.Tts.Provider))
                return;

            if (readMode is ResultReadAloudMode.Source or ResultReadAloudMode.Both)
                await EnqueueAsync(sourceText, _settings.General.SourceLanguage.Id);
            if (readMode is ResultReadAloudMode.Target or ResultReadAloudMode.Both)
                await EnqueueAsync(targetText, _settings.General.TargetLanguage.Id);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Screenshot read-aloud failed.");
        }
    }

    private async Task EnqueueAsync(string text, string languageId)
    {
        var voice = await _tts.ResolvePreferredVoiceAsync(languageId);
        if (voice.IsSuccess && !string.IsNullOrWhiteSpace(voice.Value))
            await _tts.EnqueueAsync(new TtsSynthesisRequest(text, voice.Value));
    }
}
