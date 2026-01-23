using System.Collections.Generic;

namespace EasyChat.Services.Languages;

/// <summary>
/// Service that manages the master list of supported languages and their codes for different providers.
/// </summary>
public class LanguageService
{
    private static readonly Dictionary<string, LanguageDefinition> Languages = new();
    private const string TencentProviderKey = "Tencent";
    private const string BaiduProviderKey = "Baidu";
    private const string GoogleProviderKey = "Google";
    private const string DeepLProviderKey = "DeepL";
    

    static LanguageService()
    {
        InitializeLanguages();
    }

    public static LanguageDefinition GetLanguage(string id)
    {
        if (Languages.TryGetValue(id, out var lang))
        {
            return lang;
        }

        return new LanguageDefinition(id, id, id, "unknown.png");
    }

    public static IEnumerable<LanguageDefinition> GetAllLanguages()
    {
        return Languages.Values;
    }

    private static void Register(LanguageDefinition language)
    {
        if (!Languages.ContainsKey(language.Id))
        {
            Languages.Add(language.Id, language);
        }
    }

    private static void InitializeLanguages()
    {
        // ----------------------------------------------------------------------------------
        // Common & Major Languages
        // ----------------------------------------------------------------------------------
        Register(new LanguageDefinition(LanguageKeys.AutoId, "自动检测", "Auto Detect", "auto.png")
            .WithCode(BaiduProviderKey, "auto").WithCode(TencentProviderKey, "auto").WithCode(GoogleProviderKey, "auto").WithCode(DeepLProviderKey, ""));

        Register(new LanguageDefinition(LanguageKeys.ChineseSimplifiedId, "简体中文", "Simplified Chinese", "cn.png")
            .WithCode(BaiduProviderKey, "zh").WithCode(TencentProviderKey, "zh").WithCode(GoogleProviderKey, "zh-CN").WithCode(DeepLProviderKey, "ZH"));

        Register(new LanguageDefinition(LanguageKeys.ChineseTraditionalId, "繁体中文", "Traditional Chinese", "cn.png")
            .WithCode(BaiduProviderKey, "cht").WithCode(TencentProviderKey, "zh-TW").WithCode(GoogleProviderKey, "zh-TW").WithCode(DeepLProviderKey, "ZH"));

        Register(new LanguageDefinition(LanguageKeys.EnglishId, "英语", "English", "gb.png")
            .WithCode(BaiduProviderKey, "en").WithCode(TencentProviderKey, "en").WithCode(GoogleProviderKey, "en").WithCode(DeepLProviderKey, "EN"));

        Register(new LanguageDefinition(LanguageKeys.JapaneseId, "日语", "Japanese", "jp.png")
            .WithCode(BaiduProviderKey, "jp").WithCode(TencentProviderKey, "ja").WithCode(GoogleProviderKey, "ja").WithCode(DeepLProviderKey, "JA"));

        Register(new LanguageDefinition(LanguageKeys.KoreanId, "韩语", "Korean", "kr.png")
            .WithCode(BaiduProviderKey, "kor").WithCode(TencentProviderKey, "ko").WithCode(GoogleProviderKey, "ko").WithCode(DeepLProviderKey, "KO"));

        Register(new LanguageDefinition(LanguageKeys.FrenchId, "法语", "French", "fr.png")
            .WithCode(BaiduProviderKey, "fra").WithCode(TencentProviderKey, "fr").WithCode(GoogleProviderKey, "fr").WithCode(DeepLProviderKey, "FR"));

        Register(new LanguageDefinition(LanguageKeys.SpanishId, "西班牙语", "Spanish", "es.png")
            .WithCode(BaiduProviderKey, "spa").WithCode(TencentProviderKey, "es").WithCode(GoogleProviderKey, "es").WithCode(DeepLProviderKey, "ES"));

        Register(new LanguageDefinition(LanguageKeys.GermanId, "德语", "German", "de.png")
            .WithCode(BaiduProviderKey, "de").WithCode(TencentProviderKey, "de").WithCode(GoogleProviderKey, "de").WithCode(DeepLProviderKey, "DE"));

        Register(new LanguageDefinition(LanguageKeys.RussianId, "俄语", "Russian", "ru.png")
            .WithCode(BaiduProviderKey, "ru").WithCode(TencentProviderKey, "ru").WithCode(GoogleProviderKey, "ru").WithCode(DeepLProviderKey, "RU"));

        Register(new LanguageDefinition(LanguageKeys.ItalianId, "意大利语", "Italian", "it.png")
            .WithCode(BaiduProviderKey, "it").WithCode(TencentProviderKey, "it").WithCode(GoogleProviderKey, "it").WithCode(DeepLProviderKey, "IT"));

        Register(new LanguageDefinition(LanguageKeys.PortugueseId, "葡萄牙语", "Portuguese", "pt.png")
            .WithCode(BaiduProviderKey, "pt").WithCode(TencentProviderKey, "pt").WithCode(GoogleProviderKey, "pt").WithCode(DeepLProviderKey, "PT"));

        Register(new LanguageDefinition(LanguageKeys.PortugueseBrazilId, "葡萄牙语(巴西)", "Portuguese (Brazil)", "br.png")
            .WithCode(GoogleProviderKey, "pt-BR").WithCode(DeepLProviderKey, "PT-BR"));

        Register(new LanguageDefinition(LanguageKeys.VietnameseId, "越南语", "Vietnamese", "vn.png")
            .WithCode(BaiduProviderKey, "vie").WithCode(TencentProviderKey, "vi").WithCode(GoogleProviderKey, "vi")
            .WithCode(DeepLProviderKey,
                "VI")); // Note: DeepL removed VI in some docs but kept in Markdown? Let's assume MD is right.

        Register(new LanguageDefinition(LanguageKeys.ThaiId, "泰语", "Thai", "th.png")
            .WithCode(BaiduProviderKey, "th").WithCode(TencentProviderKey, "th").WithCode(GoogleProviderKey, "th").WithCode(DeepLProviderKey, "TH"));

        Register(new LanguageDefinition(LanguageKeys.ArabicId, "阿拉伯语", "Arabic", "sa.png")
            .WithCode(BaiduProviderKey, "ara").WithCode(TencentProviderKey, "ar").WithCode(GoogleProviderKey, "ar").WithCode(DeepLProviderKey, "AR"));

        Register(new LanguageDefinition(LanguageKeys.IndonesianId, "印尼语", "Indonesian", "id.png")
            .WithCode(BaiduProviderKey, "").WithCode(TencentProviderKey, "id").WithCode(GoogleProviderKey, "id")
            .WithCode(DeepLProviderKey, "ID")); // Baidu missing?

        Register(new LanguageDefinition(LanguageKeys.MalayId, "马来语", "Malay", "my.png")
            .WithCode(BaiduProviderKey, "").WithCode(TencentProviderKey, "ms").WithCode(GoogleProviderKey, "ms")
            .WithCode(DeepLProviderKey, "MS")); // Baidu missing?

        Register(new LanguageDefinition(LanguageKeys.HindiId, "印地语", "Hindi", "in.png")
            .WithCode(BaiduProviderKey, "").WithCode(TencentProviderKey, "hi").WithCode(GoogleProviderKey, "hi")
            .WithCode(DeepLProviderKey, "HI")); // Baidu missing?

        Register(new LanguageDefinition(LanguageKeys.TurkishId, "土耳其语", "Turkish", "tr.png")
            .WithCode(TencentProviderKey, "tr").WithCode(GoogleProviderKey, "tr").WithCode(DeepLProviderKey, "TR"));

        // ----------------------------------------------------------------------------------
        // European & Others (Google & DeepL mostly)
        // ----------------------------------------------------------------------------------
        Register(new LanguageDefinition(LanguageKeys.DutchId, "荷兰语", "Dutch", "nl.png")
            .WithCode(BaiduProviderKey, "nl").WithCode(GoogleProviderKey, "nl").WithCode(DeepLProviderKey, "NL"));

        Register(new LanguageDefinition(LanguageKeys.PolishId, "波兰语", "Polish", "pl.png")
            .WithCode(BaiduProviderKey, "pl").WithCode(GoogleProviderKey, "pl").WithCode(DeepLProviderKey, "PL"));

        Register(new LanguageDefinition(LanguageKeys.UkrainianId, "乌克兰语", "Ukrainian", "ua.png")
            .WithCode(GoogleProviderKey, "uk").WithCode(DeepLProviderKey, "UK"));

        Register(new LanguageDefinition(LanguageKeys.CzechId, "捷克语", "Czech", "cz.png")
            .WithCode(BaiduProviderKey, "cs").WithCode(GoogleProviderKey, "cs").WithCode(DeepLProviderKey, "CS"));

        Register(new LanguageDefinition(LanguageKeys.HungarianId, "匈牙利语", "Hungarian", "hu.png")
            .WithCode(BaiduProviderKey, "hu").WithCode(GoogleProviderKey, "hu").WithCode(DeepLProviderKey, "HU"));

        Register(new LanguageDefinition(LanguageKeys.GreekId, "希腊语", "Greek", "gr.png")
            .WithCode(BaiduProviderKey, "el").WithCode(GoogleProviderKey, "el").WithCode(DeepLProviderKey, "EL"));

        Register(new LanguageDefinition(LanguageKeys.DanishId, "丹麦语", "Danish", "dk.png") // dk flag
            .WithCode(BaiduProviderKey, "dan").WithCode(GoogleProviderKey, "da").WithCode(DeepLProviderKey, "DA"));

        Register(new LanguageDefinition(LanguageKeys.FinnishId, "芬兰语", "Finnish", "fi.png")
            .WithCode(BaiduProviderKey, "fin").WithCode(GoogleProviderKey, "fi").WithCode(DeepLProviderKey, "FI"));

        Register(new LanguageDefinition(LanguageKeys.RomanianId, "罗马尼亚语", "Romanian", "ro.png")
            .WithCode(BaiduProviderKey, "rom").WithCode(GoogleProviderKey, "ro").WithCode(DeepLProviderKey, "RO"));

        Register(new LanguageDefinition(LanguageKeys.SwedishId, "瑞典语", "Swedish", "se.png") // se flag
            .WithCode(BaiduProviderKey, "swe").WithCode(GoogleProviderKey, "sv").WithCode(DeepLProviderKey, "SV"));

        Register(new LanguageDefinition(LanguageKeys.BulgarianId, "保加利亚语", "Bulgarian", "bg.png")
            .WithCode(BaiduProviderKey, "bul").WithCode(GoogleProviderKey, "bg").WithCode(DeepLProviderKey, "BG"));

        Register(new LanguageDefinition(LanguageKeys.EstonianId, "爱沙尼亚语", "Estonian", "ee.png") // ee flag
            .WithCode(BaiduProviderKey, "est").WithCode(GoogleProviderKey, "et").WithCode(DeepLProviderKey, "ET"));

        Register(new LanguageDefinition(LanguageKeys.SlovenianId, "斯洛文尼亚语", "Slovenian", "si.png") // si flag
            .WithCode(BaiduProviderKey, "slo").WithCode(GoogleProviderKey, "sl").WithCode(DeepLProviderKey, "SL"));

        Register(new LanguageDefinition(LanguageKeys.SlovakId, "斯洛伐克语", "Slovak", "sk.png")
            .WithCode(GoogleProviderKey, "sk").WithCode(DeepLProviderKey, "SK"));

        Register(new LanguageDefinition(LanguageKeys.LithuanianId, "立陶宛语", "Lithuanian", "lt.png")
            .WithCode(GoogleProviderKey, "lt").WithCode(DeepLProviderKey, "LT"));

        Register(new LanguageDefinition(LanguageKeys.LatvianId, "拉脱维亚语", "Latvian", "lv.png")
            .WithCode(GoogleProviderKey, "lv").WithCode(DeepLProviderKey, "LV"));

        // ----------------------------------------------------------------------------------
        // Extended List (Google mainly, mapped where possible)
        // ----------------------------------------------------------------------------------

        Register(new LanguageDefinition(LanguageKeys.AfrikaansId, "南非荷兰语", "Afrikaans", "za.png") // za flag
            .WithCode(GoogleProviderKey, "af").WithCode(DeepLProviderKey, "AF"));

        Register(new LanguageDefinition(LanguageKeys.AlbanianId, "阿尔巴尼亚语", "Albanian", "al.png")
            .WithCode(GoogleProviderKey, "sq").WithCode(DeepLProviderKey, "SQ")); // DeepL experimental usually

        Register(new LanguageDefinition(LanguageKeys.AmharicId, "阿姆哈拉语", "Amharic", "et.png") // Ethiopia
            .WithCode(GoogleProviderKey, "am"));

        Register(new LanguageDefinition(LanguageKeys.AzerbaijaniId, "阿塞拜疆语", "Azerbaijani", "az.png")
            .WithCode(GoogleProviderKey, "az").WithCode(DeepLProviderKey, "AZ"));

        Register(new LanguageDefinition(LanguageKeys.BelarusianId, "白俄罗斯语", "Belarusian", "by.png") // by flag
            .WithCode(GoogleProviderKey, "be").WithCode(DeepLProviderKey, "BE"));

        Register(new LanguageDefinition(LanguageKeys.BengaliId, "孟加拉语", "Bengali", "bd.png") // Bangladesh
            .WithCode(GoogleProviderKey, "bn").WithCode(DeepLProviderKey, "BN"));

        Register(new LanguageDefinition(LanguageKeys.BosnianId, "波斯尼亚语", "Bosnian", "ba.png") // Bosnia
            .WithCode(GoogleProviderKey, "bs").WithCode(DeepLProviderKey, "BS"));

        Register(
            new LanguageDefinition(LanguageKeys.CatalanId, "加泰罗尼亚语", "Catalan",
                    "es-ct.png") // generic es or ad? using auto generation for now, maybe use ES or AD if missing.
                .WithCode(GoogleProviderKey, "ca").WithCode(DeepLProviderKey, "CA"));

        Register(new LanguageDefinition(LanguageKeys.WelshId, "威尔士语", "Welsh", "gb-wls.png")
            .WithCode(GoogleProviderKey, "cy").WithCode(DeepLProviderKey, "CY"));

        Register(new LanguageDefinition(LanguageKeys.EsperantoId, "世界语", "Esperanto", "wo.png") // flag?
            .WithCode(DeepLProviderKey,
                "EO")); // Google might not support code directly in list? Ah, Google list didn't show EO.

        Register(new LanguageDefinition(LanguageKeys.BasqueId, "巴斯克语", "Basque", "es-pv.png")
            .WithCode(GoogleProviderKey, "eu").WithCode(DeepLProviderKey, "EU"));

        Register(new LanguageDefinition(LanguageKeys.PersianId, "波斯语", "Persian", "ir.png") // Iran
            .WithCode(GoogleProviderKey, "fa").WithCode(DeepLProviderKey, "FA")); // DeepL?

        Register(new LanguageDefinition(LanguageKeys.IrishId, "爱尔兰语", "Irish", "ie.png")
            .WithCode(GoogleProviderKey, "ga").WithCode(DeepLProviderKey, "GA"));

        Register(new LanguageDefinition(LanguageKeys.GalicianId, "加利西亚语", "Galician", "es-ga.png")
            .WithCode(GoogleProviderKey, "gl").WithCode(DeepLProviderKey, "GL"));

        Register(new LanguageDefinition(LanguageKeys.GujaratiId, "古吉拉特语", "Gujarati", "in.png")
            .WithCode(GoogleProviderKey, "gu").WithCode(DeepLProviderKey, "GU"));

        Register(new LanguageDefinition(LanguageKeys.HebrewId, "希伯来语", "Hebrew", "il.png") // Israel
            .WithCode(GoogleProviderKey, "he").WithCode(DeepLProviderKey, "HE"));

        Register(new LanguageDefinition(LanguageKeys.CroatianId, "克罗地亚语", "Croatian", "hr.png")
            .WithCode(GoogleProviderKey, "hr").WithCode(DeepLProviderKey, "HR"));

        Register(new LanguageDefinition(LanguageKeys.ArmenianId, "亚美尼亚语", "Armenian", "am.png") // Armenia
            .WithCode(GoogleProviderKey, "hy").WithCode(DeepLProviderKey, "HY"));

        Register(new LanguageDefinition(LanguageKeys.IcelandicId, "冰岛语", "Icelandic", "is.png")
            .WithCode(GoogleProviderKey, "is").WithCode(DeepLProviderKey, "IS"));

        Register(new LanguageDefinition(LanguageKeys.GeorgianId, "格鲁吉亚语", "Georgian", "ge.png")
            .WithCode(GoogleProviderKey, "ka").WithCode(DeepLProviderKey, "KA"));

        Register(new LanguageDefinition(LanguageKeys.KazakhId, "哈萨克语", "Kazakh", "kz.png")
            .WithCode(DeepLProviderKey, "KK")); // Google?

        Register(new LanguageDefinition(LanguageKeys.KhmerId, "高棉语", "Khmer", "kh.png")
            .WithCode(GoogleProviderKey, "km").WithCode(DeepLProviderKey, "KMR")); // DeepL KMR? 

        Register(new LanguageDefinition(LanguageKeys.KannadaId, "卡纳达语", "Kannada", "in.png")
            .WithCode(GoogleProviderKey, "kn"));

        Register(new LanguageDefinition(LanguageKeys.KyrgyzId, "吉尔吉斯语", "Kyrgyz", "kg.png")
            .WithCode(GoogleProviderKey, "ky").WithCode(DeepLProviderKey, "KY"));

        Register(new LanguageDefinition(LanguageKeys.LaoId, "老挝语", "Lao", "la.png")
            .WithCode(GoogleProviderKey, "lo")); // DeepL?

        Register(new LanguageDefinition(LanguageKeys.MacedonianId, "马其顿语", "Macedonian", "mk.png")
            .WithCode(GoogleProviderKey, "mk").WithCode(DeepLProviderKey, "MK"));

        Register(new LanguageDefinition(LanguageKeys.MalayalamId, "马拉雅拉姆语", "Malayalam", "in.png")
            .WithCode(GoogleProviderKey, "ml").WithCode(DeepLProviderKey, "ML"));

        Register(new LanguageDefinition(LanguageKeys.MongolianId, "蒙古语", "Mongolian", "mn.png")
            .WithCode(GoogleProviderKey, "mn").WithCode(DeepLProviderKey, "MN"));

        Register(new LanguageDefinition(LanguageKeys.MarathiId, "马拉地语", "Marathi", "in.png")
            .WithCode(GoogleProviderKey, "mr").WithCode(DeepLProviderKey, "MR"));

        Register(new LanguageDefinition(LanguageKeys.MalteseId, "马耳他语", "Maltese", "mt.png")
            .WithCode(GoogleProviderKey, "mt").WithCode(DeepLProviderKey, "MT"));

        Register(new LanguageDefinition(LanguageKeys.BurmeseId, "缅甸语", "Burmese", "mm.png")
            .WithCode(GoogleProviderKey, "my").WithCode(DeepLProviderKey, "MY"));

        Register(new LanguageDefinition(LanguageKeys.NepaliId, "尼泊尔语", "Nepali", "np.png")
            .WithCode(GoogleProviderKey, "ne").WithCode(DeepLProviderKey, "NE"));

        Register(new LanguageDefinition(LanguageKeys.NorwegianId, "挪威语", "Norwegian", "no.png")
            .WithCode(GoogleProviderKey, "no").WithCode(DeepLProviderKey, "NB")); // Google uses no, DeepL uses NB

        Register(new LanguageDefinition(LanguageKeys.PunjabiId, "旁遮普语", "Punjabi", "pk.png")
            .WithCode(GoogleProviderKey, "pa").WithCode(DeepLProviderKey, "PA"));

        Register(new LanguageDefinition(LanguageKeys.SomaliId, "索马里语", "Somali", "so.png")
            .WithCode(GoogleProviderKey, "so"));

        Register(new LanguageDefinition(LanguageKeys.AlbanianId, "阿尔巴尼亚语", "Albanian", "al.png")
            .WithCode(GoogleProviderKey, "sq").WithCode(DeepLProviderKey, "SQ"));

        Register(new LanguageDefinition(LanguageKeys.SerbianId, "塞尔维亚语", "Serbian", "rs.png")
            .WithCode(GoogleProviderKey, "sr").WithCode(DeepLProviderKey, "SR"));

        Register(new LanguageDefinition(LanguageKeys.SwahiliId, "斯瓦希里语", "Swahili", "ke.png") // Kenya or TZ
            .WithCode(GoogleProviderKey, "sw").WithCode(DeepLProviderKey, "SW"));

        Register(new LanguageDefinition(LanguageKeys.TamilId, "泰米尔语", "Tamil", "in.png")
            .WithCode(GoogleProviderKey, "ta").WithCode(DeepLProviderKey, "TA"));

        Register(new LanguageDefinition(LanguageKeys.TeluguId, "泰卢固语", "Telugu", "in.png")
            .WithCode(GoogleProviderKey, "te").WithCode(DeepLProviderKey, "TE"));

        Register(new LanguageDefinition(LanguageKeys.TajikId, "塔吉克语", "Tajik", "tj.png")
            .WithCode(GoogleProviderKey, "tg").WithCode(DeepLProviderKey, "TG"));

        Register(new LanguageDefinition(LanguageKeys.TagalogId, "菲律宾语", "Tagalog", "ph.png")
            .WithCode(GoogleProviderKey, "tl").WithCode(DeepLProviderKey, "TL"));

        Register(new LanguageDefinition(LanguageKeys.UkrainianId, "乌克兰语", "Ukrainian", "ua.png")
            .WithCode(GoogleProviderKey, "uk").WithCode(DeepLProviderKey, "UK"));

        Register(new LanguageDefinition(LanguageKeys.UrduId, "乌尔都语", "Urdu", "pk.png")
            .WithCode(GoogleProviderKey, "ur").WithCode(DeepLProviderKey, "UR"));

        Register(new LanguageDefinition(LanguageKeys.UzbekId, "乌兹别克语", "Uzbek", "uz.png")
            .WithCode(GoogleProviderKey, "uz").WithCode(DeepLProviderKey, "UZ"));

        Register(new LanguageDefinition(LanguageKeys.CantoneseId, "粤语", "Cantonese", "cn.png")
            .WithCode(BaiduProviderKey, "yue").WithCode(DeepLProviderKey, "YUE"));

        Register(new LanguageDefinition(LanguageKeys.ClassicalChineseId, "文言文", "Classical Chinese", "cn.png")
            .WithCode(BaiduProviderKey, "wyw"));
    }
}