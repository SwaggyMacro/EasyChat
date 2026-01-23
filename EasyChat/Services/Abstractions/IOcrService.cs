using Avalonia.Media.Imaging;
using EasyChat.Services.Ocr;
using System.Collections.Generic;


namespace EasyChat.Services.Abstractions;

public interface IOcrService
{
    /// <summary>
    /// Gets the name of this OCR service.
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// Gets the list of languages supported by this OCR service.
    /// </summary>
    IReadOnlyList<OcrLanguage> SupportedLanguages { get; }

    /// <summary>
    /// Recognizes text from a bitmap image.
    /// </summary>
    /// <param name="bitmap">The input image.</param>
    /// <param name="language">The target OCR language. If null, uses default language.</param>
    /// <returns>The recognized text.</returns>
    string RecognizeText(Bitmap bitmap, OcrLanguage? language = null);

    /// <summary>
    /// Recognizes text from a bitmap image and returns an annotated image with detected regions.
    /// </summary>
    /// <param name="bitmap">The input image.</param>
    /// <param name="language">The target OCR language. If null, uses default language.</param>
    /// <returns>A tuple containing the annotated image and recognized text.</returns>
    (Bitmap AnnotatedImage, string Text) RecognizeTextWithAnnotation(Bitmap bitmap, OcrLanguage? language = null);
}
