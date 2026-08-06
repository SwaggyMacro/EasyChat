using EasyChat.Contracts.Ocr;
using Sdcb.OpenVINO.PaddleOCR.Models.Online;

namespace EasyChat.Infrastructure.Windows.Ocr;

internal enum OpenVinoOcrModelFormat
{
    Paddle,
    OnnxV6
}

internal sealed record OpenVinoOcrModelPackageSpec(
    OcrModelPackage Package,
    Func<OnlineFullModels> CreateOnlineModel,
    OpenVinoOcrModelFormat Format);

internal sealed record WindowsOcrLanguageSelection(
    OcrLanguage Language,
    OpenVinoOcrModelPackageSpec Package);

internal static class OpenVinoOcrModelCatalog
{
    internal const string UniversalV6SmallId = "universal-v6-small";
    internal const string KoreanV4Id = "korean-v4";
    internal const string ArabicV4Id = "arabic-v4";
    internal const string DevanagariV4Id = "devanagari-v4";
    internal const string TamilV4Id = "tamil-v4";
    internal const string TeluguV4Id = "telugu-v4";
    internal const string KannadaV4Id = "kannada-v4";
    internal const string CyrillicV3Id = "cyrillic-v3";

    private static readonly OpenVinoOcrModelPackageSpec[] PackageSpecs =
    [
        CreateSpec(
            UniversalV6SmallId,
            () => OnlineFullModels.ChineseV6Small,
            OpenVinoOcrModelFormat.OnnxV6,
            "zh-Hans", "zh-Hant", "en", "ja", "fr", "de", "it", "es", "pt", "nl",
            "pl", "ro", "cs", "sv", "no", "da", "fi", "hu", "tr", "vi", "id", "ms",
            "az", "af", "bs", "hr", "cy", "et", "ga", "is", "ku", "lt", "lv", "mt",
            "mi", "oc", "sk", "sl", "sq", "sw", "tl", "uz", "la", "sr-Latn", "ca",
            "eu", "gl", "lb", "rm", "qu"),
        CreateSpec(KoreanV4Id, () => OnlineFullModels.KoreanV4, OpenVinoOcrModelFormat.Paddle, "ko"),
        CreateSpec(
            ArabicV4Id,
            () => OnlineFullModels.ArabicV4,
            OpenVinoOcrModelFormat.Paddle,
            "ar", "fa", "ug", "ur"),
        CreateSpec(
            DevanagariV4Id,
            () => OnlineFullModels.DevanagariV4,
            OpenVinoOcrModelFormat.Paddle,
            "hi", "mr", "ne", "bh", "mai", "ang", "bho", "mah", "sck", "new", "gom",
            "sa", "bgc"),
        CreateSpec(TamilV4Id, () => OnlineFullModels.TamilV4, OpenVinoOcrModelFormat.Paddle, "ta"),
        CreateSpec(TeluguV4Id, () => OnlineFullModels.TeluguV4, OpenVinoOcrModelFormat.Paddle, "te"),
        CreateSpec(KannadaV4Id, () => OnlineFullModels.KannadaV4, OpenVinoOcrModelFormat.Paddle, "kn"),
        CreateSpec(
            CyrillicV3Id,
            () => OnlineFullModels.CyrillicV3,
            OpenVinoOcrModelFormat.Paddle,
            "ru", "sr-Cyrl", "be", "bg", "uk", "mn", "abq", "ady", "kbd", "ava", "dar",
            "inh", "che", "lbe", "lez", "tab")
    ];

    private static readonly IReadOnlyDictionary<string, OpenVinoOcrModelPackageSpec> ByPackageId =
        PackageSpecs.ToDictionary(spec => spec.Package.Id, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, OpenVinoOcrModelPackageSpec> ByLanguageId =
        PackageSpecs
            .SelectMany(spec => spec.Package.SupportedLanguages.Select(language => (language.Id, Spec: spec)))
            .ToDictionary(item => item.Id, item => item.Spec, StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<OpenVinoOcrModelPackageSpec> Specs { get; } = PackageSpecs;

    internal static IReadOnlyList<OcrModelPackage> Packages { get; } =
        PackageSpecs.Select(spec => spec.Package).ToArray();

    internal static OpenVinoOcrModelPackageSpec ResolvePackage(OcrModelPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return ByPackageId.TryGetValue(package.Id, out var spec)
            ? spec
            : throw new ArgumentException($"Unknown OCR model package '{package.Id}'.", nameof(package));
    }

    internal static WindowsOcrLanguageSelection ResolveLanguage(OcrLanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (!OcrLanguages.TryGet(language.Id, out var canonical)
            || string.Equals(canonical.Id, OcrLanguages.Auto.Id, StringComparison.OrdinalIgnoreCase)
            || !ByLanguageId.TryGetValue(canonical.Id, out var spec))
        {
            throw new NotSupportedException($"OCR language '{language.Id}' is not supported.");
        }

        return new WindowsOcrLanguageSelection(canonical, spec);
    }

    private static OpenVinoOcrModelPackageSpec CreateSpec(
        string id,
        Func<OnlineFullModels> createOnlineModel,
        OpenVinoOcrModelFormat format,
        params string[] languageIds)
    {
        var languages = languageIds.Select(GetRequiredLanguage).ToArray();
        return new OpenVinoOcrModelPackageSpec(
            new OcrModelPackage(id, languages),
            createOnlineModel,
            format);
    }

    private static OcrLanguage GetRequiredLanguage(string id) =>
        OcrLanguages.TryGet(id, out var language)
            ? language
            : throw new InvalidOperationException($"OCR language '{id}' is missing from the contract catalog.");
}
