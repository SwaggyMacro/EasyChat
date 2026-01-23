using System.Collections.Generic;

namespace EasyChat.Services.Ocr;

/// <summary>
/// Represents a language supported by OCR services.
/// </summary>
public record OcrLanguage(string Id, string DisplayName, string? NativeName = null)
{
    public static readonly OcrLanguage ChineseSimplified = new("zh-Hans", "Chinese (Simplified)", "简体中文");
    public static readonly OcrLanguage ChineseTraditional = new("zh-Hant", "Chinese (Traditional)", "繁體中文");
    public static readonly OcrLanguage English = new("en", "English");
    public static readonly OcrLanguage Japanese = new("ja", "Japanese", "日本語");
    public static readonly OcrLanguage Korean = new("ko", "Korean", "한국어");
    public static readonly OcrLanguage Auto = new("auto", "Auto Detect", "自动检测");
    public static readonly OcrLanguage Arabic = new("ar", "Arabic", "العربية");
    public static readonly OcrLanguage Devanagari = new("hi", "Devanagari", "देवनागरी");
    public static readonly OcrLanguage Tamil = new("ta", "Tamil", "தமிழ்");
    public static readonly OcrLanguage Telugu = new("te", "Telugu", "తెలుగు");
    public static readonly OcrLanguage Kannada = new("kn", "Kannada", "ಕನ್ನಡ");

    /// <summary>
    /// All known OCR languages.
    /// </summary>
    public static readonly IReadOnlyList<OcrLanguage> All = new[]
    {
        ChineseSimplified, ChineseTraditional, English, Japanese, Korean, Auto
    };
}
