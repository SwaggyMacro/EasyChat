using System;
using System.Threading.Tasks;
using AutoMapper;
using Avalonia.Threading;
using System.Collections.Generic;
using EasyChat.Models.Configuration;
using EasyChat.Services.Abstractions;
using EasyChat.Services.Languages;
using EasyChat.Services.Ocr;
using EasyChat.Services.Translation;
using EasyChat.Services.Translation.Ai;
using EasyChat.Views.Result;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;
using EasyChat.Models;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using OpenCvSharp;
using Sdcb.PaddleOCR;
using System.Linq;
using Avalonia.Input.Platform;

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

    private void OnScreenCaptured(Bitmap bitmap, CaptureIntent intent)
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
                if (intent == CaptureIntent.CopyImageTranslated && _ocrService is PaddleOcrService paddleService)
                {
                     var result = paddleService.RecognizeTextRaw(bitmap, ocrLanguage, enableRotation: true);
                     Dispatcher.UIThread.Post(() => ProcessImageTranslation(bitmap, result));
                }
                else
                {
                    var ocrResult = _ocrService.RecognizeText(bitmap, ocrLanguage);
                    _logger.LogDebug("OCR Result Length: {Length}", ocrResult.Length);
                    Dispatcher.UIThread.Post(() => ProcessOcrResult(ocrResult, intent));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OCR processing failed.");
                Dispatcher.UIThread.Post(() => ShowError("OCR Error", ex.Message));
            }
        });
    }
    
    private void ProcessOcrResult(string ocrResult, CaptureIntent intent)
    {
        if (string.IsNullOrWhiteSpace(ocrResult))
        {
            ShowError("OCR Warning", "No text detected.");
            return;
        }

        if (intent == CaptureIntent.CopyOriginal)
        {
            CopyToClipboard(ocrResult);
            // Continue to show ResultView as requested
        }

        try
        {
            var translator = _translationServiceFactory.CreateCurrentService();
            
            // Always show ResultView
            var resultWindow = new ResultView();
            var fontSize = _configurationService.Result?.FontSize;
            resultWindow.SetFontSize(fontSize ?? 14);
            var isWindowClosed = false;
            
            resultWindow.ShowLoading(); // Show loading initially
            resultWindow.Closed += (_, _) => isWindowClosed = true;
            resultWindow.Show();

            Task.Run(async () => await TranslateAndDisplayAsync(
                ocrResult, translator, resultWindow, () => isWindowClosed, intent));
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
        Func<bool> isClosedCheck,
        CaptureIntent intent)
    {
        try
        {
            var sourceLang = _configurationService.General?.SourceLanguage;
            var targetLang = _configurationService.General?.TargetLanguage;

            _logger.LogInformation("Starting translation: {Source} -> {Target}", sourceLang?.Id, targetLang?.Id);

            if (translator is OpenAiService openAi)
            {
                await TranslateStreamingAsync(openAi, text, sourceLang, targetLang, resultWindow, isClosedCheck, intent);
            }
            else
            {
                await TranslateNonStreamingAsync(translator, text, sourceLang, targetLang, resultWindow, isClosedCheck, intent);
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
        Func<bool> isClosedCheck,
        CaptureIntent intent = CaptureIntent.Translation)
    {
        var isFirstChunk = true;
        var fullTranslation = new System.Text.StringBuilder();
        
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
            
            if (intent != CaptureIntent.Translation)
            {
                fullTranslation.Append(chunk);
            }
        }
        
        if (intent != CaptureIntent.Translation && !isClosedCheck())
        {
            var translation = fullTranslation.ToString();
            var finalText = intent == CaptureIntent.CopyBilingual 
                 ? $"{text}\n\n{translation}" 
                 : translation;
            
            // Only copy if not CopyOriginal (handled early), or if intent is explicitly CopyTranslated/Bilingual
            if (intent == CaptureIntent.CopyTranslated || intent == CaptureIntent.CopyBilingual)
            {
                 Dispatcher.UIThread.Post(() => CopyToClipboard(finalText));
            }
        }
    }

    private async Task TranslateNonStreamingAsync(
        ITranslation translator,
        string text,
        LanguageDefinition? sourceLang,
        LanguageDefinition? targetLang,
        ResultView resultWindow,
        Func<bool> isClosedCheck,
        CaptureIntent intent = CaptureIntent.Translation)
    {
        var translation = await translator.TranslateAsync(text, sourceLang, targetLang);
        
        if (!isClosedCheck() && (intent == CaptureIntent.CopyTranslated || intent == CaptureIntent.CopyBilingual))
        {
             var finalText = intent == CaptureIntent.CopyBilingual 
                 ? $"{text}\n\n{translation}" 
                 : translation;
             Dispatcher.UIThread.Post(() => CopyToClipboard(finalText));
        }
        
        Dispatcher.UIThread.Post(() =>
        {
            if (!isClosedCheck())
            {
                resultWindow.AppendText(translation);
                resultWindow.IsVisible = true;
            }
        });
    }

    // TranslateAndCopyAsync removed as we integrated it into main flow

    private void CopyToClipboard(string text)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            var clipboard = window.Clipboard;
            if (clipboard != null)
            {
                clipboard.SetTextAsync(text);
                _toastManager.CreateSimpleInfoToast()
                    .WithTitle("Copied")
                    .WithContent("Text copied to clipboard.")
                    .Queue();
            }
        }
    }

    private void ShowError(string title, string message)
    {
        _toastManager.CreateSimpleInfoToast()
            .WithTitle(title)
            .WithContent(message)
            .Queue();
    }
    private async void ProcessImageTranslation(Bitmap bitmap, PaddleOcrResult result)
    {
        try 
        {
             var regions = result.Regions;
             if (regions.Length == 0) 
             {
                 ShowError("OCR Warning", "No text detected.");
                 return;
             }
             
             // Sort regions top-bottom, left-right
             regions = regions.OrderBy(x => x.Rect.Center.Y).ThenBy(x => x.Rect.Center.X).ToArray();

             ShowError("Translating...", $"Please wait, processing {regions.Length} text regions.");

             var translator = _translationServiceFactory.CreateCurrentService();
             var sourceLang = _configurationService.General?.SourceLanguage;
             var targetLang = _configurationService.General?.TargetLanguage;
             
             // Translate
             var translationTasks = regions.Select(async r => 
             {
                 try {
                     var text = await translator.TranslateAsync(r.Text, sourceLang, targetLang);
                     return (Region: r, Text: text);
                 } catch {
                     return (Region: r, r.Text);
                 }
             });
             
             var translatedRegions = await Task.WhenAll(translationTasks);
             
             // Process Image with OpenCV
             using var mat = BitmapToMat(bitmap);
             using var mask = new Mat(mat.Size(), MatType.CV_8UC1, new Scalar(0));
             
             // Create mask from all regions
             foreach (var item in translatedRegions)
             {
                 var r = item.Region.Rect;
                 var points = r.Points().Select(p => new OpenCvSharp.Point((int)p.X, (int)p.Y)).ToArray();
                 // Inflate slightly to ensure full coverage
                 var poly = new List<List<OpenCvSharp.Point>> { points.ToList() };
                 Cv2.FillPoly(mask, poly, new Scalar(255));
             }
             
             // Dilate mask to cover edges
             using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
             Cv2.Dilate(mask, mask, kernel);
             
             // Inpaint to remove original text
             using var inpaintedMat = new Mat();
             Cv2.Inpaint(mat, mask, inpaintedMat, 3, InpaintMethod.Telea);
             
             // Convert back to Bitmap for drawing
             using var backgroundBitmap = MatToBitmap(inpaintedMat);
             
             var pixelSize = new PixelSize((int)bitmap.Size.Width, (int)bitmap.Size.Height);
             var targetBitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
             
             using (var ctx = targetBitmap.CreateDrawingContext())
             {
                 // Draw the clean background
                 ctx.DrawImage(backgroundBitmap, new Avalonia.Rect(bitmap.Size));
                 
                 foreach (var item in translatedRegions)
                 {
                     var r = item.Region.Rect;
                     var text = item.Text;
                     
                     // Calculate text metrics
                     // We want to fit the text into the rotated rect.
                     // The visual rect size in unrotated space:
                     var width = r.Size.Width;
                     var height = r.Size.Height;
                     
                     // Get average color of the region from inpainted image for contrast
                     var rect = r.BoundingRect();
                     var safeRect = rect.Intersect(new OpenCvSharp.Rect(0, 0, mat.Width, mat.Height));
                     
                     var brightness = 128.0;
                     if (safeRect.Width > 0 && safeRect.Height > 0)
                     {
                         using var roi = new Mat(inpaintedMat, safeRect);
                         var mean = Cv2.Mean(roi);
                         brightness = 0.299 * mean.Val2 + 0.587 * mean.Val1 + 0.114 * mean.Val0;
                     }
                     
                     var textColor = brightness < 128 ? Brushes.White : Brushes.Black;
                     
                     // Initial formatted text
                     var typeFace = new Typeface("Microsoft YaHei UI"); 
                     var ft = new FormattedText(
                             text,
                             System.Globalization.CultureInfo.CurrentCulture,
                             FlowDirection.LeftToRight,
                             typeFace,
                             20, // Base size to measure relative proportions
                             textColor
                         );
                     
                     // Calculate Scaling
                     // 1. Height priority: Match the height of the box
                     var scaleY = height / Math.Max(1, ft.Height);
                     
                     // 2. Width constraint: Ensure it fits in width
                     var scaleX = width / Math.Max(1, ft.Width);
                     
                     // 3. Uniform scaling: Use the smaller scale to fit inside both dimensions
                     // However, if the width difference is huge (e.g. short text in wide box), 
                     // strictly fitting height might make it super wide if we didn't use uniform.
                     // But if we use uniform based on Min, and the box is wide, we are limited by height. (Good)
                     // If the box is narrow, we are limited by width. (Good)
                     var finalScale = Math.Min(scaleX, scaleY);
                     
                     // 4. Angle Snapping
                     // Most text is horizontal. If angle is small, snap to 0 to avoid jaggedness.
                     var rotation = r.Angle;
                     if (Math.Abs(rotation) < 10) rotation = 0;
                     
                     var matrix = Matrix.CreateTranslation(-ft.Width / 2, -ft.Height / 2) // Center text at 0,0 locally
                                   * Matrix.CreateScale(finalScale, finalScale)           // Uniform Scale
                                   * Matrix.CreateRotation(rotation * Math.PI / 180.0)    // Rotate
                                   * Matrix.CreateTranslation(r.Center.X, r.Center.Y);    // Move to global position

                     using (ctx.PushTransform(matrix))
                     {
                         // Draw at 0,0 because we centered via transform
                         ctx.DrawText(ft, new Avalonia.Point(0, 0));
                     }
                 }
             }
             
             CopyImageToClipboard(targetBitmap);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Image Translation failed");
            ShowError("Image Translate Error", ex.Message);
        }
    }

    private async void CopyImageToClipboard(Bitmap bitmap)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            var clipboard = window.Clipboard;
            if (clipboard != null)
            {
                 try 
                 {
                     await clipboard.SetValueAsync(DataFormat.Bitmap, bitmap);
                     
                     _toastManager.CreateSimpleInfoToast()
                        .WithTitle("Copied")
                        .WithContent("Translated Image copied.")
                        .Queue();
                 }
                 catch (Exception ex)
                 {
                     _logger.LogError(ex, "Failed to copy image to clipboard.");
                     _toastManager.CreateSimpleInfoToast()
                        .WithTitle("Copy Failed")
                        .WithContent("Could not copy image.")
                        .Queue();
                 }
            }
        }
    }

    private static Mat BitmapToMat(Bitmap bitmap)
    {
        using var memoryStream = new System.IO.MemoryStream();
        bitmap.Save(memoryStream);
        memoryStream.Seek(0, System.IO.SeekOrigin.Begin);
        return Mat.FromStream(memoryStream, ImreadModes.Color);
    }
    
    private static Bitmap MatToBitmap(Mat mat)
    {
        using var memoryStream = new System.IO.MemoryStream();
        mat.WriteToStream(memoryStream);
        memoryStream.Seek(0, System.IO.SeekOrigin.Begin);
        return new Bitmap(memoryStream);
    }
}
