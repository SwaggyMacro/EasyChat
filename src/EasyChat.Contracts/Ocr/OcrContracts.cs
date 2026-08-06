using EasyChat.Contracts.Platform;

namespace EasyChat.Contracts.Ocr;

public sealed record OcrLanguage(string Id, string DisplayName, string? NativeName = null);

public sealed record OcrModelPackage(
    string Id,
    IReadOnlyList<OcrLanguage> SupportedLanguages);

public static class OcrLanguages
{
    public static OcrLanguage ChineseSimplified { get; } =
        new("zh-Hans", "Chinese (Simplified)", "\u7b80\u4f53\u4e2d\u6587");

    public static OcrLanguage ChineseTraditional { get; } =
        new("zh-Hant", "Chinese (Traditional)", "\u7e41\u9ad4\u4e2d\u6587");

    public static OcrLanguage English { get; } = new("en", "English");
    public static OcrLanguage Japanese { get; } = new("ja", "Japanese", "\u65e5\u672c\u8a9e");
    public static OcrLanguage Korean { get; } = new("ko", "Korean", "\ud55c\uad6d\uc5b4");
    public static OcrLanguage Auto { get; } = new("auto", "Auto Detect", "\u81ea\u52a8\u68c0\u6d4b");
    public static OcrLanguage Arabic { get; } = new("ar", "Arabic", "\u0627\u0644\u0639\u0631\u0628\u064a\u0629");
    public static OcrLanguage Devanagari { get; } = new("hi", "Devanagari", "\u0926\u0947\u0935\u0928\u093e\u0917\u0930\u0940");
    public static OcrLanguage Tamil { get; } = new("ta", "Tamil", "\u0ba4\u0bae\u0bbf\u0bb4\u0bcd");
    public static OcrLanguage Telugu { get; } = new("te", "Telugu", "\u0c24\u0c46\u0c32\u0c41\u0c17\u0c41");
    public static OcrLanguage Kannada { get; } = new("kn", "Kannada", "\u0c95\u0ca8\u0ccd\u0ca8\u0ca1");

    public static IReadOnlyList<OcrLanguage> Supported { get; } =
    [
        ChineseSimplified,
        ChineseTraditional,
        English,
        Japanese,
        new("fr", "French"),
        new("de", "German"),
        new("it", "Italian"),
        new("es", "Spanish"),
        new("pt", "Portuguese"),
        new("nl", "Dutch"),
        new("pl", "Polish"),
        new("ro", "Romanian"),
        new("cs", "Czech"),
        new("sv", "Swedish"),
        new("no", "Norwegian"),
        new("da", "Danish"),
        new("fi", "Finnish"),
        new("hu", "Hungarian"),
        new("tr", "Turkish"),
        new("vi", "Vietnamese"),
        new("id", "Indonesian"),
        new("ms", "Malay"),
        new("az", "Azerbaijani"),
        new("af", "Afrikaans"),
        new("bs", "Bosnian"),
        new("hr", "Croatian"),
        new("cy", "Welsh"),
        new("et", "Estonian"),
        new("ga", "Irish"),
        new("is", "Icelandic"),
        new("ku", "Kurdish"),
        new("lt", "Lithuanian"),
        new("lv", "Latvian"),
        new("mt", "Maltese"),
        new("mi", "Maori"),
        new("oc", "Occitan"),
        new("sk", "Slovak"),
        new("sl", "Slovenian"),
        new("sq", "Albanian"),
        new("sw", "Swahili"),
        new("tl", "Tagalog"),
        new("uz", "Uzbek"),
        new("la", "Latin"),
        new("sr-Latn", "Serbian (Latin)"),
        new("ca", "Catalan"),
        new("eu", "Basque"),
        new("gl", "Galician"),
        new("lb", "Luxembourgish"),
        new("rm", "Romansh"),
        new("qu", "Quechua"),
        Korean,
        Arabic,
        new("fa", "Persian"),
        new("ug", "Uyghur"),
        new("ur", "Urdu"),
        Devanagari,
        new("mr", "Marathi"),
        new("ne", "Nepali"),
        new("bh", "Bihari"),
        new("mai", "Maithili"),
        new("ang", "Angika"),
        new("bho", "Bhojpuri"),
        new("mah", "Magahi"),
        new("sck", "Sadri"),
        new("new", "Newari"),
        new("gom", "Konkani"),
        new("sa", "Sanskrit"),
        new("bgc", "Haryanvi"),
        Tamil,
        Telugu,
        Kannada,
        new("ru", "Russian"),
        new("sr-Cyrl", "Serbian (Cyrillic)"),
        new("be", "Belarusian"),
        new("bg", "Bulgarian"),
        new("uk", "Ukrainian"),
        new("mn", "Mongolian"),
        new("abq", "Abaza"),
        new("ady", "Adyghe"),
        new("kbd", "Kabardian"),
        new("ava", "Avar"),
        new("dar", "Dargwa"),
        new("inh", "Ingush"),
        new("che", "Chechen"),
        new("lbe", "Lak"),
        new("lez", "Lezghian"),
        new("tab", "Tabassaran")
    ];

    private static readonly IReadOnlyDictionary<string, OcrLanguage> ById =
        Supported.ToDictionary(language => language.Id, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string? id, out OcrLanguage language)
    {
        if (string.Equals(id, Auto.Id, StringComparison.OrdinalIgnoreCase))
        {
            language = Auto;
            return true;
        }

        if (string.Equals(id, "sr", StringComparison.OrdinalIgnoreCase))
            id = "sr-Cyrl";

        if (id is not null && ById.TryGetValue(id, out var resolved))
        {
            language = resolved;
            return true;
        }

        language = null!;
        return false;
    }
}

public readonly record struct ImagePoint(double X, double Y);

public sealed record OcrTextRegion(
    string Text,
    IReadOnlyList<ImagePoint> Polygon,
    double Angle,
    double Confidence = 1d);

public sealed record OcrRecognitionResult(IReadOnlyList<OcrTextRegion> Regions)
{
    public string Text => string.Join("\n", Regions.Select(region => region.Text));
}

public enum OcrRecognitionMode
{
    Fast = 0,
    Normal = 1,
    IdleRelease = 2
}

public sealed record OcrRecognitionRequest(
    ImageFrame Image,
    OcrLanguage? Language = null,
    bool EnableRotation = false,
    OcrRecognitionMode Mode = OcrRecognitionMode.Normal,
    int IdleTimeoutSeconds = 300);

public sealed record OcrModelDownloadOptions(string? ProxyUrl, bool UseProxy);

public interface IOcrRecognizer
{
    ValueTask<OcrRecognitionResult> RecognizeAsync(
        OcrRecognitionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IOcrModelStore
{
    IReadOnlyList<OcrModelPackage> ModelPackages { get; }

    bool IsModelDownloaded(OcrModelPackage package);

    Task DownloadModelAsync(
        OcrModelPackage package,
        OcrModelDownloadOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    void DeleteModel(OcrModelPackage package);
}

public interface IOcrRecognitionUseCases
{
    ValueTask<OcrRecognitionResult> RecognizeAsync(
        OcrRecognitionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IOcrModelUseCases
{
    IReadOnlyList<OcrModelPackage> ModelPackages { get; }

    bool IsModelDownloaded(OcrModelPackage package);

    Task DownloadModelAsync(
        OcrModelPackage package,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    void DeleteModel(OcrModelPackage package);
}

public sealed class OcrModelNotDownloadedException : Exception
{
    public OcrModelNotDownloadedException(OcrLanguage language)
        : base($"OCR model is not downloaded for {language.Id}.")
    {
        Language = language ?? throw new ArgumentNullException(nameof(language));
    }

    public OcrLanguage Language { get; }
}
