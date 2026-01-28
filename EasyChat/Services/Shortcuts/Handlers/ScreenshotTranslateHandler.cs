using System;
using System.Threading.Tasks;
using AutoMapper;
using Avalonia.Threading;
using EasyChat.Models.Configuration;
using EasyChat.Services.Abstractions;
using EasyChat.Services.Languages;
using EasyChat.Services.Ocr;
using EasyChat.Services.Translation;
using EasyChat.Services.Translation.Ai;
using EasyChat.Views.Result;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;

namespace EasyChat.Services.Shortcuts.Handlers;

/// <summary>
/// Handler for the Screenshot shortcut action.
/// Captures screen, performs OCR, translates text, and shows result.
/// </summary>
public class ScreenshotTranslateHandler : IShortcutActionHandler
{
    private readonly IScreenCaptureService _screenCaptureService;
    private readonly IOcrService _ocrService;
    private readonly ITranslationServiceFactory _translationServiceFactory;
    private readonly IConfigurationService _configurationService;
    private readonly ISukiToastManager _toastManager;
    private readonly IMapper _mapper;
    private readonly ILogger<ScreenshotTranslateHandler> _logger;
    
    private volatile bool _isExecuting;

    public string ActionType => "Screenshot";
    public bool PreventConcurrentExecution => true;
    public bool IsExecuting => _isExecuting;

    public ScreenshotTranslateHandler(
        IScreenCaptureService screenCaptureService,
        IOcrService ocrService,
        ITranslationServiceFactory translationServiceFactory,
        IConfigurationService configurationService,
        ISukiToastManager toastManager,
        IMapper mapper,
        ILogger<ScreenshotTranslateHandler> logger)
    {
        _screenCaptureService = screenCaptureService;
        _ocrService = ocrService;
        _translationServiceFactory = translationServiceFactory;
        _configurationService = configurationService;
        _toastManager = toastManager;
        _mapper = mapper;
        _logger = logger;
    }

    public void Execute(ShortcutParameter? parameter = null)
    {
        _isExecuting = true;
        _logger.LogInformation("Screenshot shortcut executed.");
        Dispatcher.UIThread.Post(StartScreenCapture);
    }

    private void StartScreenCapture()
    {
        var mode = _configurationService.Screenshot?.Mode ?? Constants.Constant.ScreenshotMode.Precise;
        var session = new ScreenCapture.ScreenSelectionSession(_screenCaptureService, OnScreenCaptured, OnScreenCaptureCancelled, mode);
        session.Start();
    }
    
    private void OnScreenCaptureCancelled()
    {
        _isExecuting = false;
        _logger.LogInformation("Screenshot capture cancelled.");
    }

    private void OnScreenCaptured(Avalonia.Media.Imaging.Bitmap bitmap)
    {
        // Screenshot captured successfully, allow new captures immediately
        _isExecuting = false;
        
        var sourceLang = _configurationService.General?.SourceLanguage;
        var ocrLanguage = _mapper.Map<OcrLanguage?>(sourceLang?.Id ?? LanguageKeys.ChineseSimplifiedId);
        
        // Log OCR start
        _logger.LogInformation("Starting OCR with language: {Language}", ocrLanguage);

        // Run OCR in background to avoid UI blocking
        Task.Run(() =>
        {
            try
            {
                var ocrResult = _ocrService.RecognizeText(bitmap, ocrLanguage);
                _logger.LogDebug("OCR Result Length: {Length}", ocrResult.Length);
                
                Dispatcher.UIThread.Post(() => ProcessOcrResult(ocrResult));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OCR processing failed.");
                Dispatcher.UIThread.Post(() => ShowError("OCR Error", ex.Message));
            }
        });
    }
    
    private void ProcessOcrResult(string ocrResult)
    {
        try
        {
            var translator = _translationServiceFactory.CreateCurrentService();
            var resultWindow = new ResultView();
            var fontSize = _configurationService.Result?.FontSize;
            resultWindow.SetFontSize(fontSize ?? 14);
            var isWindowClosed = false;
            
            resultWindow.ShowLoading(); // Show loading initially
            resultWindow.Closed += (_, _) => isWindowClosed = true;
            resultWindow.Show();

            Task.Run(async () => await TranslateAndDisplayAsync(
                ocrResult, translator, resultWindow, () => isWindowClosed));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize translation service or window.");
            ShowError("Service Error", ex.Message);
        }
    }

    private async Task TranslateAndDisplayAsync(
        string text,
        ITranslation translator,
        ResultView resultWindow,
        Func<bool> isClosedCheck)
    {
        try
        {
            var sourceLang = _configurationService.General?.SourceLanguage;
            var targetLang = _configurationService.General?.TargetLanguage;

            _logger.LogInformation("Starting translation: {Source} -> {Target}", sourceLang?.Id, targetLang?.Id);

            if (translator is OpenAiService openAi)
            {
                await TranslateStreamingAsync(openAi, text, sourceLang, targetLang, resultWindow, isClosedCheck);
            }
            else
            {
                await TranslateNonStreamingAsync(translator, text, sourceLang, targetLang, resultWindow, isClosedCheck);
            }

            // Auto-close after configured delay
            if (!isClosedCheck())
            {
                var delay = _configurationService.Result?.AutoCloseDelay;
                if (_configurationService.Result is { EnableAutoReadDelay: true })
                {
                    var length = text.Length;
                    var msPerChar = _configurationService.Result.MsPerChar;
                    delay = Math.Max(2000, length * msPerChar); // Minimum 2 seconds
                }
                
                resultWindow.CloseAfterDelay(delay ?? 5);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation process failed.");
            
            if (!isClosedCheck())
            {
                // Unbox generic tool types like TypeInitializationException to reveal the real cause
                var errorMsg = ex.InnerException != null 
                    ? $"{ex.Message} -> {ex.InnerException.Message}" 
                    : ex.Message;

                Dispatcher.UIThread.Post(() =>
                {
                    if (!isClosedCheck())
                    {
                        ShowError("Translation Error", errorMsg);
                        resultWindow.Close();
                    }
                });
            }
        }
    }

    private async Task TranslateStreamingAsync(
        OpenAiService openAi,
        string text,
        LanguageDefinition? sourceLang,
        LanguageDefinition? targetLang,
        ResultView resultWindow,
        Func<bool> isClosedCheck)
    {
        var isFirstChunk = true;
        
        await foreach (var chunk in openAi.StreamTranslateAsync(text, sourceLang, targetLang))
        {
            if (isClosedCheck()) break;

            if (isFirstChunk && !string.IsNullOrEmpty(chunk))
            {
                isFirstChunk = false;
                Dispatcher.UIThread.Post(() =>
                {
                    if (!isClosedCheck() && !resultWindow.IsVisible)
                    {
                        resultWindow.ShowResult(); // Switch to result view on first chunk
                        resultWindow.IsVisible = true;
                    }
                });
            }

            if (isClosedCheck()) break;

            Dispatcher.UIThread.Post(() =>
            {
                if (!isClosedCheck())
                {
                    resultWindow.AppendText(chunk);
                }
            });
        }
    }

    private async Task TranslateNonStreamingAsync(
        ITranslation translator,
        string text,
        LanguageDefinition? sourceLang,
        LanguageDefinition? targetLang,
        ResultView resultWindow,
        Func<bool> isClosedCheck)
    {
        var translation = await translator.TranslateAsync(text, sourceLang, targetLang);
        
        Dispatcher.UIThread.Post(() =>
        {
            if (!isClosedCheck())
            {
                resultWindow.AppendText(translation);
                resultWindow.IsVisible = true;
            }
        });
    }

    private void ShowError(string title, string message)
    {
        _toastManager.CreateSimpleInfoToast()
            .WithTitle(title)
            .WithContent(message)
            .Queue();
    }
}
