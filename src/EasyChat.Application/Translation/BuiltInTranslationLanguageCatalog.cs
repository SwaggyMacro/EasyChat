using EasyChat.Contracts.Translation;

namespace EasyChat.Application.Translation;

public sealed class BuiltInTranslationLanguageCatalog : ITranslationLanguageCatalog
{
    private static readonly IReadOnlyList<TranslationLanguage> Languages =
    [
        L("auto", "自动检测", "Auto Detect", "auto.png", "auto", "auto", "auto"),
        L("zh-Hans", "简体中文", "Simplified Chinese", "cn.png", "zh", "zh", "zh-CN", "ZH"),
        L("zh-Hant", "繁体中文", "Traditional Chinese", "cn.png", "cht", "zh-TW", "zh-TW", "ZH"),
        L("en", "英语", "English", "gb.png", "en", "en", "en", "EN"),
        L("ja", "日语", "Japanese", "jp.png", "jp", "ja", "ja", "JA"),
        L("ko", "韩语", "Korean", "kr.png", "kor", "ko", "ko", "KO"),
        L("fr", "法语", "French", "fr.png", "fra", "fr", "fr", "FR"),
        L("es", "西班牙语", "Spanish", "es.png", "spa", "es", "es", "ES"),
        L("de", "德语", "German", "de.png", "de", "de", "de", "DE"),
        L("ru", "俄语", "Russian", "ru.png", "ru", "ru", "ru", "RU"),
        L("it", "意大利语", "Italian", "it.png", "it", "it", "it", "IT"),
        L("pt", "葡萄牙语", "Portuguese", "pt.png", "pt", "pt", "pt", "PT"),
        L("pt-BR", "葡萄牙语(巴西)", "Portuguese (Brazil)", "br.png", google: "pt-BR", deepL: "PT-BR"),
        L("vi", "越南语", "Vietnamese", "vn.png", "vie", "vi", "vi", "VI"),
        L("th", "泰语", "Thai", "th.png", "th", "th", "th", "TH"),
        L("ar", "阿拉伯语", "Arabic", "sa.png", "ara", "ar", "ar", "AR"),
        L("id", "印尼语", "Indonesian", "id.png", tencent: "id", google: "id", deepL: "ID"),
        L("ms", "马来语", "Malay", "my.png", tencent: "ms", google: "ms", deepL: "MS"),
        L("hi", "印地语", "Hindi", "in.png", tencent: "hi", google: "hi", deepL: "HI"),
        L("tr", "土耳其语", "Turkish", "tr.png", tencent: "tr", google: "tr", deepL: "TR"),
        L("nl", "荷兰语", "Dutch", "nl.png", baidu: "nl", google: "nl", deepL: "NL"),
        L("pl", "波兰语", "Polish", "pl.png", baidu: "pl", google: "pl", deepL: "PL"),
        L("uk", "乌克兰语", "Ukrainian", "ua.png", google: "uk", deepL: "UK"),
        L("cs", "捷克语", "Czech", "cz.png", baidu: "cs", google: "cs", deepL: "CS"),
        L("hu", "匈牙利语", "Hungarian", "hu.png", baidu: "hu", google: "hu", deepL: "HU"),
        L("el", "希腊语", "Greek", "gr.png", baidu: "el", google: "el", deepL: "EL"),
        L("da", "丹麦语", "Danish", "dk.png", baidu: "dan", google: "da", deepL: "DA"),
        L("fi", "芬兰语", "Finnish", "fi.png", baidu: "fin", google: "fi", deepL: "FI"),
        L("ro", "罗马尼亚语", "Romanian", "ro.png", baidu: "rom", google: "ro", deepL: "RO"),
        L("sv", "瑞典语", "Swedish", "se.png", baidu: "swe", google: "sv", deepL: "SV"),
        L("bg", "保加利亚语", "Bulgarian", "bg.png", baidu: "bul", google: "bg", deepL: "BG"),
        L("et", "爱沙尼亚语", "Estonian", "ee.png", baidu: "est", google: "et", deepL: "ET"),
        L("sl", "斯洛文尼亚语", "Slovenian", "si.png", baidu: "slo", google: "sl", deepL: "SL"),
        L("sk", "斯洛伐克语", "Slovak", "sk.png", google: "sk", deepL: "SK"),
        L("lt", "立陶宛语", "Lithuanian", "lt.png", google: "lt", deepL: "LT"),
        L("lv", "拉脱维亚语", "Latvian", "lv.png", google: "lv", deepL: "LV"),
        L("af", "南非荷兰语", "Afrikaans", "za.png", google: "af", deepL: "AF"),
        L("sq", "阿尔巴尼亚语", "Albanian", "al.png", google: "sq", deepL: "SQ"),
        L("am", "阿姆哈拉语", "Amharic", "et.png", google: "am"),
        L("az", "阿塞拜疆语", "Azerbaijani", "az.png", google: "az", deepL: "AZ"),
        L("be", "白俄罗斯语", "Belarusian", "by.png", google: "be", deepL: "BE"),
        L("bn", "孟加拉语", "Bengali", "bd.png", google: "bn", deepL: "BN"),
        L("bs", "波斯尼亚语", "Bosnian", "ba.png", google: "bs", deepL: "BS"),
        L("ca", "加泰罗尼亚语", "Catalan", "es-ct.png", google: "ca", deepL: "CA"),
        L("cy", "威尔士语", "Welsh", "gb-wls.png", google: "cy", deepL: "CY"),
        L("eo", "世界语", "Esperanto", "wo.png", deepL: "EO"),
        L("eu", "巴斯克语", "Basque", "es-pv.png", google: "eu", deepL: "EU"),
        L("fa", "波斯语", "Persian", "ir.png", google: "fa", deepL: "FA"),
        L("ga", "爱尔兰语", "Irish", "ie.png", google: "ga", deepL: "GA"),
        L("gl", "加利西亚语", "Galician", "es-ga.png", google: "gl", deepL: "GL"),
        L("gu", "古吉拉特语", "Gujarati", "in.png", google: "gu", deepL: "GU"),
        L("he", "希伯来语", "Hebrew", "il.png", google: "he", deepL: "HE"),
        L("hr", "克罗地亚语", "Croatian", "hr.png", google: "hr", deepL: "HR"),
        L("hy", "亚美尼亚语", "Armenian", "am.png", google: "hy", deepL: "HY"),
        L("is", "冰岛语", "Icelandic", "is.png", google: "is", deepL: "IS"),
        L("ka", "格鲁吉亚语", "Georgian", "ge.png", google: "ka", deepL: "KA"),
        L("kk", "哈萨克语", "Kazakh", "kz.png", deepL: "KK"),
        L("km", "高棉语", "Khmer", "kh.png", google: "km", deepL: "KMR"),
        L("kn", "卡纳达语", "Kannada", "in.png", google: "kn"),
        L("ky", "吉尔吉斯语", "Kyrgyz", "kg.png", google: "ky", deepL: "KY"),
        L("lo", "老挝语", "Lao", "la.png", google: "lo"),
        L("mk", "马其顿语", "Macedonian", "mk.png", google: "mk", deepL: "MK"),
        L("ml", "马拉雅拉姆语", "Malayalam", "in.png", google: "ml", deepL: "ML"),
        L("mn", "蒙古语", "Mongolian", "mn.png", google: "mn", deepL: "MN"),
        L("mr", "马拉地语", "Marathi", "in.png", google: "mr", deepL: "MR"),
        L("mt", "马耳他语", "Maltese", "mt.png", google: "mt", deepL: "MT"),
        L("my", "缅甸语", "Burmese", "mm.png", google: "my", deepL: "MY"),
        L("ne", "尼泊尔语", "Nepali", "np.png", google: "ne", deepL: "NE"),
        L("no", "挪威语", "Norwegian", "no.png", google: "no", deepL: "NB"),
        L("pa", "旁遮普语", "Punjabi", "pk.png", google: "pa", deepL: "PA"),
        L("so", "索马里语", "Somali", "so.png", google: "so"),
        L("sr", "塞尔维亚语", "Serbian", "rs.png", google: "sr", deepL: "SR"),
        L("sw", "斯瓦希里语", "Swahili", "ke.png", google: "sw", deepL: "SW"),
        L("ta", "泰米尔语", "Tamil", "in.png", google: "ta", deepL: "TA"),
        L("te", "泰卢固语", "Telugu", "in.png", google: "te", deepL: "TE"),
        L("tg", "塔吉克语", "Tajik", "tj.png", google: "tg", deepL: "TG"),
        L("tl", "菲律宾语", "Tagalog", "ph.png", google: "tl", deepL: "TL"),
        L("ur", "乌尔都语", "Urdu", "pk.png", google: "ur", deepL: "UR"),
        L("uz", "乌兹别克语", "Uzbek", "uz.png", google: "uz", deepL: "UZ"),
        L("yue", "粤语", "Cantonese", "cn.png", baidu: "yue", deepL: "YUE"),
        L("wyw", "文言文", "Classical Chinese", "cn.png", baidu: "wyw"),
        L("ku", "库尔德语", "Kurdish", "unknown.png"),
        L("mi", "毛利语", "Maori", "unknown.png"),
        L("oc", "奥克语", "Occitan", "unknown.png"),
        L("la", "拉丁语", "Latin", "unknown.png"),
        L("sr-Latn", "塞尔维亚语（拉丁字母）", "Serbian (Latin)", "rs.png", google: "sr", deepL: "SR"),
        L("sr-Cyrl", "塞尔维亚语（西里尔字母）", "Serbian (Cyrillic)", "rs.png", google: "sr", deepL: "SR"),
        L("lb", "卢森堡语", "Luxembourgish", "unknown.png"),
        L("rm", "罗曼什语", "Romansh", "unknown.png"),
        L("qu", "克丘亚语", "Quechua", "unknown.png"),
        L("ug", "维吾尔语", "Uyghur", "cn.png", google: "ug"),
        L("bh", "比哈尔语", "Bihari", "in.png"),
        L("mai", "迈蒂利语", "Maithili", "in.png"),
        L("ang", "安吉卡语", "Angika", "in.png"),
        L("bho", "博杰普尔语", "Bhojpuri", "in.png"),
        L("mah", "摩揭陀语", "Magahi", "in.png"),
        L("sck", "萨德里语", "Sadri", "in.png"),
        L("new", "尼瓦尔语", "Newari", "np.png"),
        L("gom", "孔卡尼语", "Konkani", "in.png"),
        L("sa", "梵语", "Sanskrit", "in.png"),
        L("bgc", "哈里亚纳语", "Haryanvi", "in.png"),
        L("abq", "阿巴扎语", "Abaza", "unknown.png"),
        L("ady", "阿迪格语", "Adyghe", "unknown.png"),
        L("kbd", "卡巴尔达语", "Kabardian", "unknown.png"),
        L("ava", "阿瓦尔语", "Avar", "unknown.png"),
        L("dar", "达尔格瓦语", "Dargwa", "unknown.png"),
        L("inh", "印古什语", "Ingush", "unknown.png"),
        L("che", "车臣语", "Chechen", "unknown.png"),
        L("lbe", "拉克语", "Lak", "unknown.png"),
        L("lez", "列兹金语", "Lezghian", "unknown.png"),
        L("tab", "塔巴萨兰语", "Tabassaran", "unknown.png")
    ];

    private static readonly IReadOnlyDictionary<string, TranslationLanguage> ById =
        Languages.ToDictionary(language => language.Id, StringComparer.Ordinal);

    public IReadOnlyList<TranslationLanguage> All => Languages;

    public TranslationLanguage Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return ById.TryGetValue(id, out var language)
            ? language
            : new TranslationLanguage(id, id, id, Icon: "unknown.png");
    }

    private static TranslationLanguage L(
        string id,
        string nativeName,
        string englishName,
        string icon,
        string? baidu = null,
        string? tencent = null,
        string? google = null,
        string? deepL = null)
    {
        var codes = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(codes, MachineTranslationProviderNames.Baidu, baidu);
        Add(codes, MachineTranslationProviderNames.Tencent, tencent);
        Add(codes, MachineTranslationProviderNames.Google, google);
        Add(codes, MachineTranslationProviderNames.DeepL, deepL);
        return new TranslationLanguage(id, englishName, nativeName, codes, icon);
    }

    private static void Add(Dictionary<string, string> codes, string provider, string? code)
    {
        if (!string.IsNullOrWhiteSpace(code))
            codes.Add(provider, code);
    }
}
