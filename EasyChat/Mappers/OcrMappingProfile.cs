using AutoMapper;
using EasyChat.Services.Languages;
using EasyChat.Services.Ocr;

namespace EasyChat.Mappers;

/// <summary>
/// AutoMapper profile for OCR-related mappings.
/// </summary>
public class OcrMappingProfile : Profile
{
    public OcrMappingProfile()
    {
        // Map LanguageKeys.Id (string) -> OcrLanguage
        CreateMap<string, OcrLanguage?>().ConvertUsing<LanguageIdToOcrLanguageConverter>();
    }
}

/// <summary>
/// Converts language ID string to OcrLanguage.
/// </summary>
public class LanguageIdToOcrLanguageConverter : ITypeConverter<string, OcrLanguage?>
{
    // public static readonly OcrLanguage ChineseSimplified = new("zh-Hans", "Chinese (Simplified)", "简体中文");
    // public static readonly OcrLanguage ChineseTraditional = new("zh-Hant", "Chinese (Traditional)", "繁體中文");
    // public static readonly OcrLanguage English = new("en", "English");
    // public static readonly OcrLanguage Japanese = new("ja", "Japanese", "日本語");
    // public static readonly OcrLanguage Korean = new("ko", "Korean", "한국어");
    // public static readonly OcrLanguage Auto = new("auto", "Auto Detect", "自动检测");
    // public static readonly OcrLanguage Arabic = new("ar", "Arabic", "العربية");
    // public static readonly OcrLanguage Devanagari = new("hi", "Devanagari", "देवनागरी");
    // public static readonly OcrLanguage Tamil = new("ta", "Tamil", "தமிழ்");
    // public static readonly OcrLanguage Telugu = new("te", "Telugu", "తెలుగు");
    // public static readonly OcrLanguage Kannada = new("kn", "Kannada", "ಕನ್ನಡ");
    public OcrLanguage? Convert(string source, OcrLanguage? destination, ResolutionContext context)
    {
        return source switch
        {
            LanguageKeys.ChineseSimplifiedId => OcrLanguage.ChineseSimplified,
            LanguageKeys.ChineseTraditionalId => OcrLanguage.ChineseTraditional,
            LanguageKeys.EnglishId => OcrLanguage.English,
            LanguageKeys.JapaneseId => OcrLanguage.Japanese,
            LanguageKeys.KoreanId => OcrLanguage.Korean,
            LanguageKeys.AutoId => OcrLanguage.Auto,
            LanguageKeys.ArabicId => OcrLanguage.Arabic,
            LanguageKeys.HindiId => OcrLanguage.Devanagari,
            LanguageKeys.TamilId => OcrLanguage.Tamil,
            LanguageKeys.TeluguId => OcrLanguage.Telugu,
            LanguageKeys.KannadaId => OcrLanguage.Kannada,
            _ => null
        };
    }
}
