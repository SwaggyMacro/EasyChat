namespace EasyChat.Services.Languages;

/// <summary>
/// Static helper class to access well-known language definitions.
/// </summary>
public static class LanguageKeys
{
    #region Language ID Constants

    public const string AutoId = "auto";
    public const string ChineseSimplifiedId = "zh-Hans";
    public const string ChineseTraditionalId = "zh-Hant";
    public const string EnglishId = "en";
    public const string JapaneseId = "ja";
    public const string KoreanId = "ko";
    public const string FrenchId = "fr";
    public const string SpanishId = "es";
    public const string GermanId = "de";
    public const string RussianId = "ru";
    public const string ItalianId = "it";
    public const string PortugueseId = "pt";
    public const string PortugueseBrazilId = "pt-BR";
    public const string VietnameseId = "vi";
    public const string ThaiId = "th";
    public const string ArabicId = "ar";
    public const string IndonesianId = "id";
    public const string MalayId = "ms";
    public const string HindiId = "hi";
    public const string TurkishId = "tr";
    public const string DutchId = "nl";
    public const string PolishId = "pl";
    public const string UkrainianId = "uk";
    public const string CzechId = "cs";
    public const string HungarianId = "hu";
    public const string GreekId = "el";
    public const string DanishId = "da";
    public const string FinnishId = "fi";
    public const string RomanianId = "ro";
    public const string SwedishId = "sv";
    public const string BulgarianId = "bg";
    public const string EstonianId = "et";
    public const string SlovenianId = "sl";
    public const string SlovakId = "sk";
    public const string LithuanianId = "lt";
    public const string LatvianId = "lv";
    public const string AfrikaansId = "af";
    public const string AlbanianId = "sq";
    public const string AmharicId = "am";
    public const string AzerbaijaniId = "az";
    public const string BelarusianId = "be";
    public const string BengaliId = "bn";
    public const string BosnianId = "bs";
    public const string CatalanId = "ca";
    public const string WelshId = "cy";
    public const string EsperantoId = "eo";
    public const string BasqueId = "eu";
    public const string PersianId = "fa";
    public const string IrishId = "ga";
    public const string GalicianId = "gl";
    public const string GujaratiId = "gu";
    public const string HebrewId = "he";
    public const string CroatianId = "hr";
    public const string ArmenianId = "hy";
    public const string IcelandicId = "is";
    public const string GeorgianId = "ka";
    public const string KazakhId = "kk";
    public const string KhmerId = "km";
    public const string KannadaId = "kn";
    public const string KyrgyzId = "ky";
    public const string LaoId = "lo";
    public const string MacedonianId = "mk";
    public const string MalayalamId = "ml";
    public const string MongolianId = "mn";
    public const string MarathiId = "mr";
    public const string MalteseId = "mt";
    public const string BurmeseId = "my";
    public const string NepaliId = "ne";
    public const string NorwegianId = "no";
    public const string PunjabiId = "pa";
    public const string SomaliId = "so";
    public const string SerbianId = "sr";
    public const string SwahiliId = "sw";
    public const string TamilId = "ta";
    public const string TeluguId = "te";
    public const string TajikId = "tg";
    public const string TagalogId = "tl";
    public const string UrduId = "ur";
    public const string UzbekId = "uz";
    public const string CantoneseId = "yue";
    public const string ClassicalChineseId = "wyw";

    #endregion

    #region Language Definition Accessors

    public static LanguageDefinition Auto => LanguageService.GetLanguage(AutoId);
    public static LanguageDefinition ChineseSimplified => LanguageService.GetLanguage(ChineseSimplifiedId);
    public static LanguageDefinition ChineseTraditional => LanguageService.GetLanguage(ChineseTraditionalId);
    public static LanguageDefinition English => LanguageService.GetLanguage(EnglishId);
    public static LanguageDefinition Japanese => LanguageService.GetLanguage(JapaneseId);
    public static LanguageDefinition Korean => LanguageService.GetLanguage(KoreanId);
    public static LanguageDefinition French => LanguageService.GetLanguage(FrenchId);
    public static LanguageDefinition Spanish => LanguageService.GetLanguage(SpanishId);
    public static LanguageDefinition German => LanguageService.GetLanguage(GermanId);
    public static LanguageDefinition Russian => LanguageService.GetLanguage(RussianId);
    public static LanguageDefinition Italian => LanguageService.GetLanguage(ItalianId);
    public static LanguageDefinition Portuguese => LanguageService.GetLanguage(PortugueseId);
    public static LanguageDefinition Vietnamese => LanguageService.GetLanguage(VietnameseId);
    public static LanguageDefinition Thai => LanguageService.GetLanguage(ThaiId);
    public static LanguageDefinition Arabic => LanguageService.GetLanguage(ArabicId);
    public static LanguageDefinition Polish => LanguageService.GetLanguage(PolishId);
    public static LanguageDefinition Dutch => LanguageService.GetLanguage(DutchId);

    #endregion
}
