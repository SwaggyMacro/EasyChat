using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace EasyChat.Services.Speech.Tts;

public static class TtsLanguageService
{
    [SuppressMessage("ReSharper", "InconsistentNaming")] 
    public static class Locales
    {
        public const string Af_ZA = "af-ZA";
        public const string Am_ET = "am-ET";
        public const string Ar_AE = "ar-AE";
        public const string Ar_BH = "ar-BH";
        public const string Ar_DZ = "ar-DZ";
        public const string Ar_EG = "ar-EG";
        public const string Ar_IQ = "ar-IQ";
        public const string Ar_JO = "ar-JO";
        public const string Ar_KW = "ar-KW";
        public const string Ar_LB = "ar-LB";
        public const string Ar_LY = "ar-LY";
        public const string Ar_MA = "ar-MA";
        public const string Ar_OM = "ar-OM";
        public const string Ar_QA = "ar-QA";
        public const string Ar_SA = "ar-SA";
        public const string Ar_SY = "ar-SY";
        public const string Ar_TN = "ar-TN";
        public const string Ar_YE = "ar-YE";
        public const string Az_AZ = "az-AZ";
        public const string Bg_BG = "bg-BG";
        public const string Bn_BD = "bn-BD";
        public const string Bn_IN = "bn-IN";
        public const string Bs_BA = "bs-BA";
        public const string Ca_ES = "ca-ES";
        public const string Cs_CZ = "cs-CZ";
        public const string Cy_GB = "cy-GB";
        public const string Da_DK = "da-DK";
        public const string De_AT = "de-AT";
        public const string De_CH = "de-CH";
        public const string De_DE = "de-DE";
        public const string El_GR = "el-GR";
        public const string En_AU = "en-AU";
        public const string En_CA = "en-CA";
        public const string En_GB = "en-GB";
        public const string En_HK = "en-HK";
        public const string En_IE = "en-IE";
        public const string En_IN = "en-IN";
        public const string En_KE = "en-KE";
        public const string En_NG = "en-NG";
        public const string En_NZ = "en-NZ";
        public const string En_PH = "en-PH";
        public const string En_SG = "en-SG";
        public const string En_TZ = "en-TZ";
        public const string En_US = "en-US";
        public const string En_ZA = "en-ZA";
        public const string Es_AR = "es-AR";
        public const string Es_BO = "es-BO";
        public const string Es_CL = "es-CL";
        public const string Es_CO = "es-CO";
        public const string Es_CR = "es-CR";
        public const string Es_CU = "es-CU";
        public const string Es_DO = "es-DO";
        public const string Es_EC = "es-EC";
        public const string Es_ES = "es-ES";
        public const string Es_GQ = "es-GQ";
        public const string Es_GT = "es-GT";
        public const string Es_HN = "es-HN";
        public const string Es_MX = "es-MX";
        public const string Es_NI = "es-NI";
        public const string Es_PA = "es-PA";
        public const string Es_PE = "es-PE";
        public const string Es_PR = "es-PR";
        public const string Es_PY = "es-PY";
        public const string Es_SV = "es-SV";
        public const string Es_US = "es-US";
        public const string Es_UY = "es-UY";
        public const string Es_VE = "es-VE";
        public const string Et_EE = "et-EE";
        public const string Fa_IR = "fa-IR";
        public const string Fi_FI = "fi-FI";
        public const string Fil_PH = "fil-PH";
        public const string Fr_BE = "fr-BE";
        public const string Fr_CA = "fr-CA";
        public const string Fr_CH = "fr-CH";
        public const string Fr_FR = "fr-FR";
        public const string Ga_IE = "ga-IE";
        public const string Gl_ES = "gl-ES";
        public const string Gu_IN = "gu-IN";
        public const string He_IL = "he-IL";
        public const string Hi_IN = "hi-IN";
        public const string Hr_HR = "hr-HR";
        public const string Hu_HU = "hu-HU";
        public const string Id_ID = "id-ID";
        public const string Is_IS = "is-IS";
        public const string It_IT = "it-IT";
        public const string Iu_Cans_CA = "iu-Cans-CA";
        public const string Iu_Latn_CA = "iu-Latn-CA";
        public const string Ja_JP = "ja-JP";
        public const string Jv_ID = "jv-ID";
        public const string Ka_GE = "ka-GE";
        public const string Kk_KZ = "kk-KZ";
        public const string Km_KH = "km-KH";
        public const string Kn_IN = "kn-IN";
        public const string Ko_KR = "ko-KR";
        public const string Lo_LA = "lo-LA";
        public const string Lt_LT = "lt-LT";
        public const string Lv_LV = "lv-LV";
        public const string Mk_MK = "mk-MK";
        public const string Ml_IN = "ml-IN";
        public const string Mn_MN = "mn-MN";
        public const string Mr_IN = "mr-IN";
        public const string Ms_MY = "ms-MY";
        public const string Mt_MT = "mt-MT";
        public const string My_MM = "my-MM";
        public const string Nb_NO = "nb-NO";
        public const string Ne_NP = "ne-NP";
        public const string Nl_BE = "nl-BE";
        public const string Nl_NL = "nl-NL";
        public const string Pl_PL = "pl-PL";
        public const string Ps_AF = "ps-AF";
        public const string Pt_BR = "pt-BR";
        public const string Pt_PT = "pt-PT";
        public const string Ro_RO = "ro-RO";
        public const string Ru_RU = "ru-RU";
        public const string Si_LK = "si-LK";
        public const string Sk_SK = "sk-SK";
        public const string Sl_SI = "sl-SI";
        public const string So_SO = "so-SO";
        public const string Sq_AL = "sq-AL";
        public const string Sr_RS = "sr-RS";
        public const string Su_ID = "su-ID";
        public const string Sv_SE = "sv-SE";
        public const string Sw_KE = "sw-KE";
        public const string Sw_TZ = "sw-TZ";
        public const string Ta_IN = "ta-IN";
        public const string Ta_LK = "ta-LK";
        public const string Ta_MY = "ta-MY";
        public const string Ta_SG = "ta-SG";
        public const string Te_IN = "te-IN";
        public const string Th_TH = "th-TH";
        public const string Tr_TR = "tr-TR";
        public const string Uk_UA = "uk-UA";
        public const string Ur_IN = "ur-IN";
        public const string Ur_PK = "ur-PK";
        public const string Uz_UZ = "uz-UZ";
        public const string Vi_VN = "vi-VN";
        public const string Zh_CN = "zh-CN";
        public const string Zh_CN_liaoning = "zh-CN-liaoning";
        public const string Zh_CN_shaanxi = "zh-CN-shaanxi";
        public const string Zh_HK = "zh-HK";
        public const string Zh_TW = "zh-TW";
        public const string Zu_ZA = "zu-ZA";
    }

    public static readonly List<TtsLanguageDefinition> Languages = new()
    {
        new TtsLanguageDefinition(Locales.Af_ZA, "Afrikaans", "SouthAfrica", "Afrikaans (SouthAfrica)", "南非荷兰语 (南非)", "za.png"),
        new TtsLanguageDefinition(Locales.Am_ET, "Amharic", "Ethiopia", "Amharic (Ethiopia)", "阿姆哈拉语 (埃塞俄比亚)", "et.png"),
        new TtsLanguageDefinition(Locales.Ar_AE, "Arabic", "UAE", "Arabic (UAE)", "阿拉伯语 (阿联酋)", "ae.png"),
        new TtsLanguageDefinition(Locales.Ar_BH, "Arabic", "Bahrain", "Arabic (Bahrain)", "阿拉伯语 (巴林)", "bh.png"),
        new TtsLanguageDefinition(Locales.Ar_DZ, "Arabic", "Algeria", "Arabic (Algeria)", "阿拉伯语 (阿尔及利亚)", "dz.png"),
        new TtsLanguageDefinition(Locales.Ar_EG, "Arabic", "Egypt", "Arabic (Egypt)", "阿拉伯语 (埃及)", "eg.png"),
        new TtsLanguageDefinition(Locales.Ar_IQ, "Arabic", "Iraq", "Arabic (Iraq)", "阿拉伯语 (伊拉克)", "iq.png"),
        new TtsLanguageDefinition(Locales.Ar_JO, "Arabic", "Jordan", "Arabic (Jordan)", "阿拉伯语 (约旦)", "jo.png"),
        new TtsLanguageDefinition(Locales.Ar_KW, "Arabic", "Kuwait", "Arabic (Kuwait)", "阿拉伯语 (科威特)", "kw.png"),
        new TtsLanguageDefinition(Locales.Ar_LB, "Arabic", "Lebanon", "Arabic (Lebanon)", "阿拉伯语 (黎巴嫩)", "lb.png"),
        new TtsLanguageDefinition(Locales.Ar_LY, "Arabic", "Libya", "Arabic (Libya)", "阿拉伯语 (利比亚)", "ly.png"),
        new TtsLanguageDefinition(Locales.Ar_MA, "Arabic", "Morocco", "Arabic (Morocco)", "阿拉伯语 (摩洛哥)", "ma.png"),
        new TtsLanguageDefinition(Locales.Ar_OM, "Arabic", "Oman", "Arabic (Oman)", "阿拉伯语 (阿曼)", "om.png"),
        new TtsLanguageDefinition(Locales.Ar_QA, "Arabic", "Qatar", "Arabic (Qatar)", "阿拉伯语 (卡塔尔)", "qa.png"),
        new TtsLanguageDefinition(Locales.Ar_SA, "Arabic", "SaudiArabia", "Arabic (SaudiArabia)", "阿拉伯语 (沙特阿拉伯)", "sa.png"),
        new TtsLanguageDefinition(Locales.Ar_SY, "Arabic", "Syria", "Arabic (Syria)", "阿拉伯语 (叙利亚)", "sy.png"),
        new TtsLanguageDefinition(Locales.Ar_TN, "Arabic", "Tunisia", "Arabic (Tunisia)", "阿拉伯语 (突尼斯)", "tn.png"),
        new TtsLanguageDefinition(Locales.Ar_YE, "Arabic", "Yemen", "Arabic (Yemen)", "阿拉伯语 (也门)", "ye.png"),
        new TtsLanguageDefinition(Locales.Az_AZ, "Azerbaijani", "Azerbaijan", "Azerbaijani (Azerbaijan)", "阿塞拜疆语 (阿塞拜疆)", "az.png"),
        new TtsLanguageDefinition(Locales.Bg_BG, "Bulgarian", "Bulgaria", "Bulgarian (Bulgaria)", "保加利亚语 (保加利亚)", "bg.png"),
        new TtsLanguageDefinition(Locales.Bn_BD, "Bengali", "Bangladesh", "Bengali (Bangladesh)", "孟加拉语 (孟加拉国)", "bd.png"),
        new TtsLanguageDefinition(Locales.Bn_IN, "Bengali", "India", "Bengali (India)", "孟加拉语 (印度)", "in.png"),
        new TtsLanguageDefinition(Locales.Bs_BA, "Bosnian", "Bosnia", "Bosnian (Bosnia)", "波斯尼亚语 (波斯尼亚)", "ba.png"),
        new TtsLanguageDefinition(Locales.Ca_ES, "Catalan", "Spain", "Catalan (Spain)", "加泰罗尼亚语 (西班牙)", "es.png"),
        new TtsLanguageDefinition(Locales.Cs_CZ, "Czech", "CzechRepublic", "Czech (CzechRepublic)", "捷克语 (捷克)", "cz.png"),
        new TtsLanguageDefinition(Locales.Cy_GB, "Welsh", "UnitedKingdom", "Welsh (UnitedKingdom)", "威尔士语 (英国)", "gb.png"),
        new TtsLanguageDefinition(Locales.Da_DK, "Danish", "Denmark", "Danish (Denmark)", "丹麦语 (丹麦)", "dk.png"),
        new TtsLanguageDefinition(Locales.De_AT, "German", "Austria", "German (Austria)", "德语 (奥地利)", "at.png"),
        new TtsLanguageDefinition(Locales.De_CH, "German", "Switzerland", "German (Switzerland)", "德语 (瑞士)", "ch.png"),
        new TtsLanguageDefinition(Locales.De_DE, "German", "Germany", "German (Germany)", "德语 (德国)", "de.png"),
        new TtsLanguageDefinition(Locales.El_GR, "Greek", "Greece", "Greek (Greece)", "希腊语 (希腊)", "gr.png"),
        new TtsLanguageDefinition(Locales.En_AU, "English", "Australia", "English (Australia)", "英语 (澳大利亚)", "au.png"),
        new TtsLanguageDefinition(Locales.En_CA, "English", "Canada", "English (Canada)", "英语 (加拿大)", "ca.png"),
        new TtsLanguageDefinition(Locales.En_GB, "English", "UnitedKingdom", "English (UnitedKingdom)", "英语 (英国)", "gb.png"),
        new TtsLanguageDefinition(Locales.En_HK, "English", "HongKong", "English (HongKong)", "英语 (中国香港)", "hk.png"),
        new TtsLanguageDefinition(Locales.En_IE, "English", "Ireland", "English (Ireland)", "英语 (爱尔兰)", "ie.png"),
        new TtsLanguageDefinition(Locales.En_IN, "English", "India", "English (India)", "英语 (印度)", "in.png"),
        new TtsLanguageDefinition(Locales.En_KE, "English", "Kenya", "English (Kenya)", "英语 (肯尼亚)", "ke.png"),
        new TtsLanguageDefinition(Locales.En_NG, "English", "Nigeria", "English (Nigeria)", "英语 (尼日利亚)", "ng.png"),
        new TtsLanguageDefinition(Locales.En_NZ, "English", "NewZealand", "English (NewZealand)", "英语 (新西兰)", "nz.png"),
        new TtsLanguageDefinition(Locales.En_PH, "English", "Philippines", "English (Philippines)", "英语 (菲律宾)", "ph.png"),
        new TtsLanguageDefinition(Locales.En_SG, "English", "Singapore", "English (Singapore)", "英语 (新加坡)", "sg.png"),
        new TtsLanguageDefinition(Locales.En_TZ, "English", "Tanzania", "English (Tanzania)", "英语 (坦桑尼亚)", "tz.png"),
        new TtsLanguageDefinition(Locales.En_US, "English", "UnitedStates", "English (UnitedStates)", "英语 (美国)", "us.png"),
        new TtsLanguageDefinition(Locales.En_ZA, "English", "SouthAfrica", "English (SouthAfrica)", "英语 (南非)", "za.png"),
        new TtsLanguageDefinition(Locales.Es_AR, "Spanish", "Argentina", "Spanish (Argentina)", "西班牙语 (阿根廷)", "ar.png"),
        new TtsLanguageDefinition(Locales.Es_BO, "Spanish", "Bolivia", "Spanish (Bolivia)", "西班牙语 (玻利维亚)", "bo.png"),
        new TtsLanguageDefinition(Locales.Es_CL, "Spanish", "Chile", "Spanish (Chile)", "西班牙语 (智利)", "cl.png"),
        new TtsLanguageDefinition(Locales.Es_CO, "Spanish", "Colombia", "Spanish (Colombia)", "西班牙语 (哥伦比亚)", "co.png"),
        new TtsLanguageDefinition(Locales.Es_CR, "Spanish", "CostaRica", "Spanish (CostaRica)", "西班牙语 (哥斯达黎加)", "cr.png"),
        new TtsLanguageDefinition(Locales.Es_CU, "Spanish", "Cuba", "Spanish (Cuba)", "西班牙语 (古巴)", "cu.png"),
        new TtsLanguageDefinition(Locales.Es_DO, "Spanish", "DominicanRepublic", "Spanish (DominicanRepublic)", "西班牙语 (多米尼加)", "do.png"),
        new TtsLanguageDefinition(Locales.Es_EC, "Spanish", "Ecuador", "Spanish (Ecuador)", "西班牙语 (厄瓜多尔)", "ec.png"),
        new TtsLanguageDefinition(Locales.Es_ES, "Spanish", "Spain", "Spanish (Spain)", "西班牙语 (西班牙)", "es.png"),
        new TtsLanguageDefinition(Locales.Es_GQ, "Spanish", "EquatorialGuinea", "Spanish (EquatorialGuinea)", "西班牙语 (赤道几内亚)", "gq.png"),
        new TtsLanguageDefinition(Locales.Es_GT, "Spanish", "Guatemala", "Spanish (Guatemala)", "西班牙语 (危地马拉)", "gt.png"),
        new TtsLanguageDefinition(Locales.Es_HN, "Spanish", "Honduras", "Spanish (Honduras)", "西班牙语 (洪都拉斯)", "hn.png"),
        new TtsLanguageDefinition(Locales.Es_MX, "Spanish", "Mexico", "Spanish (Mexico)", "西班牙语 (墨西哥)", "mx.png"),
        new TtsLanguageDefinition(Locales.Es_NI, "Spanish", "Nicaragua", "Spanish (Nicaragua)", "西班牙语 (尼加拉瓜)", "ni.png"),
        new TtsLanguageDefinition(Locales.Es_PA, "Spanish", "Panama", "Spanish (Panama)", "西班牙语 (巴拿马)", "pa.png"),
        new TtsLanguageDefinition(Locales.Es_PE, "Spanish", "Peru", "Spanish (Peru)", "西班牙语 (秘鲁)", "pe.png"),
        new TtsLanguageDefinition(Locales.Es_PR, "Spanish", "PuertoRico", "Spanish (PuertoRico)", "西班牙语 (波多黎各)", "pr.png"),
        new TtsLanguageDefinition(Locales.Es_PY, "Spanish", "Paraguay", "Spanish (Paraguay)", "西班牙语 (巴拉圭)", "py.png"),
        new TtsLanguageDefinition(Locales.Es_SV, "Spanish", "ElSalvador", "Spanish (ElSalvador)", "西班牙语 (萨尔瓦多)", "sv.png"),
        new TtsLanguageDefinition(Locales.Es_US, "Spanish", "UnitedStates", "Spanish (UnitedStates)", "西班牙语 (美国)", "us.png"),
        new TtsLanguageDefinition(Locales.Es_UY, "Spanish", "Uruguay", "Spanish (Uruguay)", "西班牙语 (乌拉圭)", "uy.png"),
        new TtsLanguageDefinition(Locales.Es_VE, "Spanish", "Venezuela", "Spanish (Venezuela)", "西班牙语 (委内瑞拉)", "ve.png"),
        new TtsLanguageDefinition(Locales.Et_EE, "Estonian", "Estonia", "Estonian (Estonia)", "爱沙尼亚语 (爱沙尼亚)", "ee.png"),
        new TtsLanguageDefinition(Locales.Fa_IR, "Persian", "Iran", "Persian (Iran)", "波斯语 (伊朗)", "ir.png"),
        new TtsLanguageDefinition(Locales.Fi_FI, "Finnish", "Finland", "Finnish (Finland)", "芬兰语 (芬兰)", "fi.png"),
        new TtsLanguageDefinition(Locales.Fil_PH, "Filipino", "Philippines", "Filipino (Philippines)", "菲律宾语 (菲律宾)", "ph.png"),
        new TtsLanguageDefinition(Locales.Fr_BE, "French", "Belgium", "French (Belgium)", "法语 (比利时)", "be.png"),
        new TtsLanguageDefinition(Locales.Fr_CA, "French", "Canada", "French (Canada)", "法语 (加拿大)", "ca.png"),
        new TtsLanguageDefinition(Locales.Fr_CH, "French", "Switzerland", "French (Switzerland)", "法语 (瑞士)", "ch.png"),
        new TtsLanguageDefinition(Locales.Fr_FR, "French", "France", "French (France)", "法语 (法国)", "fr.png"),
        new TtsLanguageDefinition(Locales.Ga_IE, "Irish", "Ireland", "Irish (Ireland)", "爱尔兰语 (爱尔兰)", "ie.png"),
        new TtsLanguageDefinition(Locales.Gl_ES, "Galician", "Spain", "Galician (Spain)", "加利西亚语 (西班牙)", "es.png"),
        new TtsLanguageDefinition(Locales.Gu_IN, "Gujarati", "India", "Gujarati (India)", "古吉拉特语 (印度)", "in.png"),
        new TtsLanguageDefinition(Locales.He_IL, "Hebrew", "Israel", "Hebrew (Israel)", "希伯来语 (以色列)", "il.png"),
        new TtsLanguageDefinition(Locales.Hi_IN, "Hindi", "India", "Hindi (India)", "印地语 (印度)", "in.png"),
        new TtsLanguageDefinition(Locales.Hr_HR, "Croatian", "Croatia", "Croatian (Croatia)", "克罗地亚语 (克罗地亚)", "hr.png"),
        new TtsLanguageDefinition(Locales.Hu_HU, "Hungarian", "Hungary", "Hungarian (Hungary)", "匈牙利语 (匈牙利)", "hu.png"),
        new TtsLanguageDefinition(Locales.Id_ID, "Indonesian", "Indonesia", "Indonesian (Indonesia)", "印度尼西亚语 (印度尼西亚)", "id.png"),
        new TtsLanguageDefinition(Locales.Is_IS, "Icelandic", "Iceland", "Icelandic (Iceland)", "冰岛语 (冰岛)", "is.png"),
        new TtsLanguageDefinition(Locales.It_IT, "Italian", "Italy", "Italian (Italy)", "意大利语 (意大利)", "it.png"),
        new TtsLanguageDefinition(Locales.Iu_Cans_CA, "Inuktitut", "CA-Cans", "Inuktitut (CA-Cans)", "因纽特语 (CA-Cans)", "ca-cans.png"),
        new TtsLanguageDefinition(Locales.Iu_Latn_CA, "Inuktitut", "CA-Latn", "Inuktitut (CA-Latn)", "因纽特语 (CA-Latn)", "ca-latn.png"),
        new TtsLanguageDefinition(Locales.Ja_JP, "Japanese", "Japan", "Japanese (Japan)", "日语 (日本)", "jp.png"),
        new TtsLanguageDefinition(Locales.Jv_ID, "Javanese", "Indonesia", "Javanese (Indonesia)", "爪哇语 (印度尼西亚)", "id.png"),
        new TtsLanguageDefinition(Locales.Ka_GE, "Georgian", "Georgia", "Georgian (Georgia)", "格鲁吉亚语 (格鲁吉亚)", "ge.png"),
        new TtsLanguageDefinition(Locales.Kk_KZ, "Kazakh", "Kazakhstan", "Kazakh (Kazakhstan)", "哈萨克语 (哈萨克斯坦)", "kz.png"),
        new TtsLanguageDefinition(Locales.Km_KH, "Khmer", "Cambodia", "Khmer (Cambodia)", "高棉语 (柬埔寨)", "kh.png"),
        new TtsLanguageDefinition(Locales.Kn_IN, "Kannada", "India", "Kannada (India)", "卡纳达语 (印度)", "in.png"),
        new TtsLanguageDefinition(Locales.Ko_KR, "Korean", "Korea", "Korean (Korea)", "韩语 (韩国)", "kr.png"),
        new TtsLanguageDefinition(Locales.Lo_LA, "Lao", "Laos", "Lao (Laos)", "老挝语 (老挝)", "la.png"),
        new TtsLanguageDefinition(Locales.Lt_LT, "Lithuanian", "Lithuania", "Lithuanian (Lithuania)", "立陶宛语 (立陶宛)", "lt.png"),
        new TtsLanguageDefinition(Locales.Lv_LV, "Latvian", "Latvia", "Latvian (Latvia)", "拉脱维亚语 (拉脱维亚)", "lv.png"),
        new TtsLanguageDefinition(Locales.Mk_MK, "Macedonian", "NorthMacedonia", "Macedonian (NorthMacedonia)", "马其顿语 (北马其顿)", "mk.png"),
        new TtsLanguageDefinition(Locales.Ml_IN, "Malayalam", "India", "Malayalam (India)", "马拉雅拉姆语 (印度)", "in.png"),
        new TtsLanguageDefinition(Locales.Mn_MN, "Mongolian", "Mongolia", "Mongolian (Mongolia)", "蒙古语 (蒙古)", "mn.png"),
        new TtsLanguageDefinition(Locales.Mr_IN, "Marathi", "India", "Marathi (India)", "马拉地语 (印度)", "in.png"),
        new TtsLanguageDefinition(Locales.Ms_MY, "Malay", "Malaysia", "Malay (Malaysia)", "马来语 (马来西亚)", "my.png"),
        new TtsLanguageDefinition(Locales.Mt_MT, "Maltese", "Malta", "Maltese (Malta)", "马耳他语 (马耳他)", "mt.png"),
        new TtsLanguageDefinition(Locales.My_MM, "Burmese", "Myanmar", "Burmese (Myanmar)", "缅甸语 (缅甸)", "mm.png"),
        new TtsLanguageDefinition(Locales.Nb_NO, "Norwegian", "Norway", "Norwegian (Norway)", "挪威语 (挪威)", "no.png"),
        new TtsLanguageDefinition(Locales.Ne_NP, "Nepali", "Nepal", "Nepali (Nepal)", "尼泊尔语 (尼泊尔)", "np.png"),
        new TtsLanguageDefinition(Locales.Nl_BE, "Dutch", "Belgium", "Dutch (Belgium)", "荷兰语 (比利时)", "be.png"),
        new TtsLanguageDefinition(Locales.Nl_NL, "Dutch", "Netherlands", "Dutch (Netherlands)", "荷兰语 (荷兰)", "nl.png"),
        new TtsLanguageDefinition(Locales.Pl_PL, "Polish", "Poland", "Polish (Poland)", "波兰语 (波兰)", "pl.png"),
        new TtsLanguageDefinition(Locales.Ps_AF, "Pashto", "Afghanistan", "Pashto (Afghanistan)", "普什图语 (阿富汗)", "af.png"),
        new TtsLanguageDefinition(Locales.Pt_BR, "Portuguese", "Brazil", "Portuguese (Brazil)", "葡萄牙语 (巴西)", "br.png"),
        new TtsLanguageDefinition(Locales.Pt_PT, "Portuguese", "Portugal", "Portuguese (Portugal)", "葡萄牙语 (葡萄牙)", "pt.png"),
        new TtsLanguageDefinition(Locales.Ro_RO, "Romanian", "Romania", "Romanian (Romania)", "罗马尼亚语 (罗马尼亚)", "ro.png"),
        new TtsLanguageDefinition(Locales.Ru_RU, "Russian", "Russia", "Russian (Russia)", "俄语 (俄罗斯)", "ru.png"),
        new TtsLanguageDefinition(Locales.Si_LK, "Sinhala", "SriLanka", "Sinhala (SriLanka)", "僧伽罗语 (斯里兰卡)", "lk.png"),
        new TtsLanguageDefinition(Locales.Sk_SK, "Slovak", "Slovakia", "Slovak (Slovakia)", "斯洛伐克语 (斯洛伐克)", "sk.png"),
        new TtsLanguageDefinition(Locales.Sl_SI, "Slovenian", "Slovenia", "Slovenian (Slovenia)", "斯洛文尼亚语 (斯洛文尼亚)", "si.png"),
        new TtsLanguageDefinition(Locales.So_SO, "Somali", "Somalia", "Somali (Somalia)", "索马里语 (索马里)", "so.png"),
        new TtsLanguageDefinition(Locales.Sq_AL, "Albanian", "Albania", "Albanian (Albania)", "阿尔巴尼亚语 (阿尔巴尼亚)", "al.png"),
        new TtsLanguageDefinition(Locales.Sr_RS, "Serbian", "Serbia", "Serbian (Serbia)", "塞尔维亚语 (塞尔维亚)", "rs.png"),
        new TtsLanguageDefinition(Locales.Su_ID, "Sundanese", "Indonesia", "Sundanese (Indonesia)", "巽他语 (印度尼西亚)", "id.png"),
        new TtsLanguageDefinition(Locales.Sv_SE, "Swedish", "Sweden", "Swedish (Sweden)", "瑞典语 (瑞典)", "se.png"),
        new TtsLanguageDefinition(Locales.Sw_KE, "Swahili", "Kenya", "Swahili (Kenya)", "斯瓦希里语 (肯尼亚)", "ke.png"),
        new TtsLanguageDefinition(Locales.Sw_TZ, "Swahili", "Tanzania", "Swahili (Tanzania)", "斯瓦希里语 (坦桑尼亚)", "tz.png"),
        new TtsLanguageDefinition(Locales.Ta_IN, "Tamil", "India", "Tamil (India)", "泰米尔语 (印度)", "in.png"),
        new TtsLanguageDefinition(Locales.Ta_LK, "Tamil", "SriLanka", "Tamil (SriLanka)", "泰米尔语 (斯里兰卡)", "lk.png"),
        new TtsLanguageDefinition(Locales.Ta_MY, "Tamil", "Malaysia", "Tamil (Malaysia)", "泰米尔语 (马来西亚)", "my.png"),
        new TtsLanguageDefinition(Locales.Ta_SG, "Tamil", "Singapore", "Tamil (Singapore)", "泰米尔语 (新加坡)", "sg.png"),
        new TtsLanguageDefinition(Locales.Te_IN, "Telugu", "India", "Telugu (India)", "泰卢固语 (印度)", "in.png"),
        new TtsLanguageDefinition(Locales.Th_TH, "Thai", "Thailand", "Thai (Thailand)", "泰语 (泰国)", "th.png"),
        new TtsLanguageDefinition(Locales.Tr_TR, "Turkish", "Turkey", "Turkish (Turkey)", "土耳其语 (土耳其)", "tr.png"),
        new TtsLanguageDefinition(Locales.Uk_UA, "Ukrainian", "Ukraine", "Ukrainian (Ukraine)", "乌克兰语 (乌克兰)", "ua.png"),
        new TtsLanguageDefinition(Locales.Ur_IN, "Urdu", "India", "Urdu (India)", "乌尔都语 (印度)", "in.png"),
        new TtsLanguageDefinition(Locales.Ur_PK, "Urdu", "Pakistan", "Urdu (Pakistan)", "乌尔都语 (巴基斯坦)", "pk.png"),
        new TtsLanguageDefinition(Locales.Uz_UZ, "Uzbek", "Uzbekistan", "Uzbek (Uzbekistan)", "乌兹别克语 (乌兹别克斯坦)", "uz.png"),
        new TtsLanguageDefinition(Locales.Vi_VN, "Vietnamese", "Vietnam", "Vietnamese (Vietnam)", "越南语 (越南)", "vn.png"),
        new TtsLanguageDefinition(Locales.Zh_CN, "Chinese", "China", "Chinese (China)", "中文 (中国)", "cn.png"),
        new TtsLanguageDefinition(Locales.Zh_CN_liaoning, "Chinese", "CN-Liaoning", "Chinese (CN-Liaoning)", "中文 (CN-Liaoning)", "cn-liaoning.png"),
        new TtsLanguageDefinition(Locales.Zh_CN_shaanxi, "Chinese", "CN-Shaanxi", "Chinese (CN-Shaanxi)", "中文 (CN-Shaanxi)", "cn-shaanxi.png"),
        new TtsLanguageDefinition(Locales.Zh_HK, "Chinese", "HongKong", "Chinese (HongKong)", "中文 (中国香港)", "hk.png"),
        new TtsLanguageDefinition(Locales.Zh_TW, "Chinese", "Taiwan", "Chinese (Taiwan)", "中文 (中国台湾)", "tw.png"),
        new TtsLanguageDefinition(Locales.Zu_ZA, "Zulu", "SouthAfrica", "Zulu (SouthAfrica)", "祖鲁语 (南非)", "za.png"),
    };
}
