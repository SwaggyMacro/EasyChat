using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using EasyChat.Services.Abstractions;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace EasyChat.Services.Ocr;

public class PaddleOcrService : IOcrService, IDisposable
{
    private readonly ILogger<PaddleOcrService> _logger;
    private readonly ConcurrentDictionary<OcrLanguage, Lazy<PaddleOcrAll>> _engines = new();

    public string ServiceName => "PaddleOCR";

    public IReadOnlyList<OcrLanguage> SupportedLanguages { get; } =
    [
        OcrLanguage.ChineseSimplified,
        OcrLanguage.ChineseTraditional,
        OcrLanguage.English,
        OcrLanguage.Japanese,
        OcrLanguage.Korean,
        OcrLanguage.Arabic,
        OcrLanguage.Devanagari,
        OcrLanguage.Tamil,
        OcrLanguage.Telugu,
        OcrLanguage.Kannada
    ];

    private static readonly Dictionary<OcrLanguage, Func<FullOcrModel>> ModelFactories = new()
    {
        [OcrLanguage.ChineseSimplified] = () => LocalFullModels.ChineseV5,
        [OcrLanguage.ChineseTraditional] = () => LocalFullModels.TraditionalChineseV3,
        [OcrLanguage.English] = () => LocalFullModels.EnglishV4,
        [OcrLanguage.Japanese] = () => LocalFullModels.JapanV4,
        [OcrLanguage.Korean] = () => LocalFullModels.KoreanV4,
        [OcrLanguage.Arabic] = () => LocalFullModels.ArabicV4,
        [OcrLanguage.Devanagari] = () => LocalFullModels.DevanagariV4,
        [OcrLanguage.Tamil] = () => LocalFullModels.TamilV4,
        [OcrLanguage.Telugu] = () => LocalFullModels.TeluguV4,
        [OcrLanguage.Kannada] = () => LocalFullModels.KannadaV4
    };

    public PaddleOcrService(ILogger<PaddleOcrService> logger)
    {
        _logger = logger;
    }

    public void Dispose()
    {
        foreach (var kvp in _engines)
        {
            if (kvp.Value.IsValueCreated)
            {
                kvp.Value.Value.Dispose();
            }
        }
        _engines.Clear();
        _logger.LogDebug("PaddleOcrService disposed");
    }

    public string RecognizeText(Bitmap bitmap, OcrLanguage? language = null)
    {
        var lang = language ?? OcrLanguage.ChineseSimplified;
        _logger.LogDebug("Starting OCR recognition with language: {Language}", lang.DisplayName);
        
        var engine = GetOrCreateEngine(lang);
        using var src = BitmapToMat(bitmap);
        var result = engine.Run(src);
        
        _logger.LogInformation("OCR ({Language}): {CharCount} characters recognized", 
            lang.DisplayName, result.Text.Length);
        GC.Collect();
        return result.Text;
    }

    public (Bitmap AnnotatedImage, string Text) RecognizeTextWithAnnotation(Bitmap bitmap, OcrLanguage? language = null)
    {
        var lang = language ?? OcrLanguage.ChineseSimplified;
        _logger.LogDebug("Starting OCR recognition with annotation, language: {Language}", lang.DisplayName);
        
        var engine = GetOrCreateEngine(lang);
        using var src = BitmapToMat(bitmap);
        var result = engine.Run(src);

        // Annotate logic (drawing rectangles)
        foreach (var region in result.Regions)
            src.Rectangle(region.Rect.BoundingRect().TopLeft, region.Rect.BoundingRect().BottomRight, Scalar.Red, 2);

        var annotatedBitmap = MatToBitmap(src);
        _logger.LogInformation("OCR with annotation ({Language}): {CharCount} characters, {RegionCount} regions", 
            lang.DisplayName, result.Text.Length, result.Regions.Length);
        return (annotatedBitmap, result.Text);
    }

    public PaddleOcrResult RecognizeTextRaw(Bitmap bitmap, OcrLanguage? language = null, bool enableRotation = false)
    {
        var lang = language ?? OcrLanguage.ChineseSimplified;
        _logger.LogDebug("Starting raw OCR recognition with language: {Language}, Rotation: {Rotation}", lang.DisplayName, enableRotation);
        
        var engine = GetOrCreateEngine(lang);
        
        // Save previous state
        var oldRotate = engine.AllowRotateDetection;
        var old180 = engine.Enable180Classification;

        if (enableRotation)
        {
            engine.AllowRotateDetection = true;
            // engine.Enable180Classification = true; // Causing crash
        }

        try
        {
            using var src = BitmapToMat(bitmap);
            return engine.Run(src);
        }
        finally
        {
            // Restore state
            if (enableRotation)
            {
                engine.AllowRotateDetection = oldRotate;
                // engine.Enable180Classification = old180;
            }
        }
    }

    private PaddleOcrAll GetOrCreateEngine(OcrLanguage language)
    {
        return _engines.GetOrAdd(language, lang => new Lazy<PaddleOcrAll>(() =>
        {
            _logger.LogInformation("Initializing PaddleOCR engine for {Language}...", lang.DisplayName);
            var model = GetModel(lang);
            var engine = new PaddleOcrAll(model, PaddleDevice.Onnx())
            {
                AllowRotateDetection = false,
                Enable180Classification = false
            };
            _logger.LogInformation("PaddleOCR engine for {Language} initialized successfully", lang.DisplayName);
            return engine;
        })).Value;
    }

    private static FullOcrModel GetModel(OcrLanguage language)
        => ModelFactories.TryGetValue(language, out var factory) ? factory() : LocalFullModels.ChineseV4;

    private static Mat BitmapToMat(Bitmap bitmap)
    {
        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin);
        return Mat.FromStream(memoryStream, ImreadModes.Color);
    }

    private static Bitmap MatToBitmap(Mat mat)
    {
        using var memoryStream = new MemoryStream();
        mat.WriteToStream(memoryStream, ".jpg");
        memoryStream.Seek(0, SeekOrigin.Begin);
        return new Bitmap(memoryStream);
    }
}
