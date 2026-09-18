using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Speech;
using EasyChat.Presentation.Foundation.Localization;
using EasyChat.Presentation.Lang;
using Material.Icons;

namespace EasyChat.Presentation.Foundation.Formatting;

public static class AiModelTypeConverters
{
    public static readonly IValueConverter ToIcon = new AiModelTypeToIconConverter();
    public static readonly IValueConverter ToDisplayName = new AiModelTypeToDisplayNameConverter();
    public static readonly IValueConverter HasIcon = new AiModelTypeHasIconConverter();
    public static readonly IValueConverter HasNoIcon = new AiModelTypeHasNoIconConverter();
    public static readonly IValueConverter IsOpenAi = new AiModelTypeMatchConverter(AiModelType.OpenAi);
    public static readonly IValueConverter IsGoogle = new AiModelTypeMatchConverter(AiModelType.Google);
    public static readonly IValueConverter IsClaude = new AiModelTypeMatchConverter(AiModelType.Claude);

    public static string GetDisplayName(AiModelType modelType, CultureInfo? uiCulture = null)
    {
        var (chineseName, englishName) = GetNames(modelType);
        return LanguageDisplayNames.ForUi(chineseName, englishName, uiCulture);
    }

    public static (string ChineseName, string EnglishName) GetNames(AiModelType modelType) => modelType switch
    {
        AiModelType.OpenAi => ("OpenAI", "OpenAI"),
        AiModelType.Google => ("Google", "Google"),
        AiModelType.Claude => ("Claude", "Claude"),
        AiModelType.DeepSeek => ("DeepSeek", "DeepSeek"),
        AiModelType.Qwen => ("通义千问", "Qwen"),
        AiModelType.Zhipu => ("智谱 AI", "Zhipu AI"),
        AiModelType.Moonshot => ("月之暗面 Kimi", "Moonshot Kimi"),
        AiModelType.Doubao => ("字节跳动豆包", "ByteDance Doubao"),
        AiModelType.MiniMax => ("MiniMax", "MiniMax"),
        AiModelType.Hunyuan => ("腾讯混元", "Tencent Hunyuan"),
        AiModelType.Grok => ("Grok", "Grok"),
        AiModelType.Mistral => ("Mistral AI", "Mistral AI"),
        AiModelType.Qianfan => ("百度千帆", "Baidu Qianfan"),
        AiModelType.Spark => ("讯飞星火", "iFlytek Spark"),
        AiModelType.StepFun => ("阶跃星辰", "StepFun"),
        AiModelType.ModelScope => ("魔搭 ModelScope", "ModelScope"),
        AiModelType.SiliconFlow => ("硅基流动", "SiliconFlow"),
        AiModelType.XiaomiMimo => ("小米", "XiaoMi"),
        AiModelType.OpenRouter => ("OpenRouter", "OpenRouter"),
        AiModelType.Together => ("Together AI", "Together AI"),
        AiModelType.Fireworks => ("Fireworks AI", "Fireworks AI"),
        AiModelType.Groq => ("Groq", "Groq"),
        AiModelType.Cerebras => ("Cerebras", "Cerebras"),
        AiModelType.DeepInfra => ("DeepInfra", "DeepInfra"),
        AiModelType.NvidiaNim => ("NVIDIA NIM", "NVIDIA NIM"),
        AiModelType.Custom => ("自定义", "Custom"),
        _ => ("未知", "Unknown")
    };
}

public sealed class AiModelTypeToDisplayNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is AiModelType modelType ? AiModelTypeConverters.GetDisplayName(modelType, culture) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class AiModelTypeHasIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is AiModelType modelType
        && AiModelTypeConverters.ToIcon.Convert(modelType, targetType, parameter, culture) is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class AiModelTypeHasNoIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is AiModelType
        && !((bool)new AiModelTypeHasIconConverter().Convert(value, targetType, parameter, culture));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class AiModelTypeToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is AiModelType modelType
            ? AssetIconLoader.Load(modelType switch
            {
                AiModelType.OpenAi => "openai.png",
                AiModelType.Google => "gemini.png",
                AiModelType.Claude => "claude.png",
                AiModelType.DeepSeek => "deepseek.png",
                AiModelType.Qwen => "qwen.png",
                AiModelType.Zhipu => "zhipu.png",
                AiModelType.Moonshot => "kimi.png",
                AiModelType.Doubao => "doubao.png",
                AiModelType.MiniMax => "minimax.png",
                AiModelType.Hunyuan => "hunyuan.png",
                AiModelType.Grok => "grok.png",
                AiModelType.Mistral => "mistral.png",
                AiModelType.Qianfan => "qianfan.png",
                AiModelType.Spark => "spark.png",
                AiModelType.StepFun => "stepfun.png",
                AiModelType.ModelScope => "modelscope.png",
                AiModelType.SiliconFlow => "siliconflow.png",
                AiModelType.XiaomiMimo => "xiaomi.png",
                AiModelType.OpenRouter => "openrouter.png",
                AiModelType.Together => "together.png",
                AiModelType.Fireworks => "fireworks.png",
                AiModelType.Groq => "groq.png",
                AiModelType.Cerebras => "cerebras.png",
                AiModelType.DeepInfra => "deepinfra.png",
                AiModelType.NvidiaNim => "nvidia.png",
                AiModelType.Custom => "custom.png",
                _ => null
            })
            : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class AiModelTypeMatchConverter(AiModelType expected) : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is AiModelType actual && actual == expected;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public static class EngineConverters
{
    public static readonly IValueConverter ToIcon = new EngineNameToIconConverter();
    public static readonly IValueConverter HasIcon = new EngineHasIconConverter();
    public static readonly IValueConverter HasNoIcon = new EngineHasNoIconConverter();
}

public sealed class EngineNameToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        AssetIconLoader.Load(AssetIconLoader.ResolveEngineFile(value as string));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class EngineHasIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        AssetIconLoader.Exists(AssetIconLoader.ResolveEngineFile(value as string));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class EngineHasNoIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !AssetIconLoader.Exists(AssetIconLoader.ResolveEngineFile(value as string));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public static class EngineTypeToBoolConverters
{
    public static readonly IValueConverter AiModel = new EngineTypeToBoolConverter("AiModel");
    public static readonly IValueConverter MachineTrans = new EngineTypeToBoolConverter("MachineTrans");
}

public sealed class EngineTypeToBoolConverter(string expected) : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string actual && actual == expected;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? expected : BindingOperations.DoNothing;
}

public static class LanguageFlagConverters
{
    public static readonly IValueConverter ToIcon = new LanguageFlagToIconConverter();
    public static readonly IValueConverter HasIcon = new LanguageFlagHasIconConverter();
    public static readonly IValueConverter HasNoIcon = new LanguageFlagHasNoIconConverter();
}

public static class LanguageSettingsConverters
{
    public static readonly IValueConverter ToDisplayName = new LanguageSettingsDisplayNameConverter();
}

public static class SpeechRecognitionModelConverters
{
    public static readonly IValueConverter ToDisplayName = new SpeechRecognitionModelDisplayNameConverter();
}

public sealed class SpeechRecognitionModelDisplayNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is SpeechRecognitionModel model
            ? LanguageDisplayNames.ForUi(model.ChineseName, model.EnglishName)
            : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class LanguageSettingsDisplayNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is LanguageSettings language
            ? LanguageDisplayNames.ForUi(language.ChineseName, language.EnglishName)
            : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class LanguageFlagToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return LanguageFlagAssetLoader.Load(value as string);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class LanguageFlagHasIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        LanguageFlagAssetLoader.Exists(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class LanguageFlagHasNoIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !LanguageFlagAssetLoader.Exists(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class TranslationEngineStringToIconKindConverter : IValueConverter
{
    public static readonly TranslationEngineStringToIconKindConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string provider)
        {
            if (provider.Equals("AiModel", StringComparison.OrdinalIgnoreCase)
                || provider.Equals(Resources.AIEngine, StringComparison.OrdinalIgnoreCase))
                return MaterialIconKind.Robot;
            if (provider.Equals("MachineTrans", StringComparison.OrdinalIgnoreCase)
                || provider.Equals(Resources.MachineTranslation, StringComparison.OrdinalIgnoreCase))
                return MaterialIconKind.Translate;
        }

        return MaterialIconKind.HelpCircleOutline;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

internal static class AssetIconLoader
{
    public static string? ResolveEngineFile(string? engine) => engine?.ToLowerInvariant() switch
    {
        "baidu" => "Baidu.png",
        "tencent" => "Tencent.png",
        "google" => "Google.png",
        "deepl" => "DeepL.png",
        "bing" => "Bing.png",
        "youdao" => "Youdao.png",
        _ when engine?.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) == true => "openai.png",
        _ when engine?.Contains("Gemini", StringComparison.OrdinalIgnoreCase) == true => "gemini.png",
        _ when engine?.Contains("Claude", StringComparison.OrdinalIgnoreCase) == true => "claude.png",
        _ when engine?.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase) == true => "deepseek.png",
        _ when engine?.Contains("通义", StringComparison.OrdinalIgnoreCase) == true || engine?.Contains("Qwen", StringComparison.OrdinalIgnoreCase) == true => "qwen.png",
        _ when engine?.Contains("智谱", StringComparison.OrdinalIgnoreCase) == true || engine?.Contains("GLM", StringComparison.OrdinalIgnoreCase) == true => "zhipu.png",
        _ when engine?.Contains("月之暗面", StringComparison.OrdinalIgnoreCase) == true || engine?.Contains("Kimi", StringComparison.OrdinalIgnoreCase) == true => "kimi.png",
        _ when engine?.Contains("豆包", StringComparison.OrdinalIgnoreCase) == true || engine?.Contains("Doubao", StringComparison.OrdinalIgnoreCase) == true => "doubao.png",
        _ when engine?.Contains("MiniMax", StringComparison.OrdinalIgnoreCase) == true => "minimax.png",
        _ when engine?.Contains("混元", StringComparison.OrdinalIgnoreCase) == true || engine?.Contains("Hunyuan", StringComparison.OrdinalIgnoreCase) == true => "hunyuan.png",
        _ when engine?.Contains("Grok", StringComparison.OrdinalIgnoreCase) == true => "grok.png",
        _ when engine?.Contains("Mistral", StringComparison.OrdinalIgnoreCase) == true => "mistral.png",
        _ when engine?.Contains("千帆", StringComparison.OrdinalIgnoreCase) == true || engine?.Contains("Qianfan", StringComparison.OrdinalIgnoreCase) == true => "qianfan.png",
        _ when engine?.Contains("星火", StringComparison.OrdinalIgnoreCase) == true || engine?.Contains("Spark", StringComparison.OrdinalIgnoreCase) == true => "spark.png",
        _ when engine?.Contains("阶跃", StringComparison.OrdinalIgnoreCase) == true || engine?.Contains("StepFun", StringComparison.OrdinalIgnoreCase) == true => "stepfun.png",
        _ when engine?.Contains("魔搭", StringComparison.OrdinalIgnoreCase) == true || engine?.Contains("ModelScope", StringComparison.OrdinalIgnoreCase) == true => "modelscope.png",
        _ when engine?.Contains("硅基", StringComparison.OrdinalIgnoreCase) == true || engine?.Contains("SiliconFlow", StringComparison.OrdinalIgnoreCase) == true => "siliconflow.png",
        _ when engine?.Contains("小米", StringComparison.OrdinalIgnoreCase) == true || engine?.Contains("MiMo", StringComparison.OrdinalIgnoreCase) == true => "xiaomi-mimo.png",
        _ when engine?.Contains("OpenRouter", StringComparison.OrdinalIgnoreCase) == true => "openrouter.png",
        _ when engine?.Contains("Together", StringComparison.OrdinalIgnoreCase) == true => "together.png",
        _ when engine?.Contains("Fireworks", StringComparison.OrdinalIgnoreCase) == true => "fireworks.png",
        _ when engine?.Contains("Groq", StringComparison.OrdinalIgnoreCase) == true => "groq.png",
        _ when engine?.Contains("Cerebras", StringComparison.OrdinalIgnoreCase) == true => "cerebras.png",
        _ when engine?.Contains("DeepInfra", StringComparison.OrdinalIgnoreCase) == true => "deepinfra.png",
        _ when engine?.Contains("NVIDIA NIM", StringComparison.OrdinalIgnoreCase) == true || engine?.Contains("NIM", StringComparison.OrdinalIgnoreCase) == true => "nvidia-nim.png",
        _ => null
    };

    public static Bitmap? Load(string? file)
    {
        if (file is null)
            return null;

        try
        {
            using var stream = AssetLoader.Open(
                new Uri($"avares://EasyChat.Desktop/Assets/Images/Engine/{file}"));
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    public static bool Exists(string? file)
    {
        if (file is null)
            return false;

        try
        {
            using var stream = AssetLoader.Open(
                new Uri($"avares://EasyChat.Desktop/Assets/Images/Engine/{file}"));
            return stream.CanRead;
        }
        catch
        {
            return false;
        }
    }
}

internal static class LanguageFlagAssetLoader
{
    // Pronunciation controls are small but frequently rendered on high-DPI displays. Decode
    // from the normal-resolution asset instead of the 40px mini asset, then downsample once.
    private const string AssetRoot = "avares://EasyChat.Desktop/Assets/Images/Flags/normal/";

    public static Bitmap? Load(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
            return null;

        try
        {
            using var stream = AssetLoader.Open(new Uri($"{AssetRoot}{file}"));
            return Bitmap.DecodeToHeight(stream, 64, BitmapInterpolationMode.HighQuality);
        }
        catch
        {
            return null;
        }
    }

    public static bool Exists(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
            return false;

        try
        {
            return AssetLoader.Exists(new Uri($"{AssetRoot}{file}"));
        }
        catch
        {
            return false;
        }
    }
}
