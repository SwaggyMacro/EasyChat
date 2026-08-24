using System.Globalization;
using Avalonia.Data.Converters;
using EasyChat.Contracts.ImageTranslation;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Settings;
using EasyChat.Presentation.Lang;

namespace EasyChat.Presentation.Features.Settings;

public sealed class DeliveryModeToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is InputDeliveryMode.Type or InputDeliveryMode.Message;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class DeliveryModeToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        InputDeliveryMode.Type => Resources.DeliveryMode_Type,
        InputDeliveryMode.Paste => Resources.DeliveryMode_Paste,
        InputDeliveryMode.Message => Resources.DeliveryMode_Message,
        _ => value?.ToString()
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ResultReadAloudModeConverter : IValueConverter
{
    public static readonly ResultReadAloudModeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ResultReadAloudMode.None => Resources.ReadAloudMode_None,
        ResultReadAloudMode.Source => Resources.ReadAloudMode_Source,
        ResultReadAloudMode.Target => Resources.ReadAloudMode_Target,
        ResultReadAloudMode.Both => Resources.ReadAloudMode_Both,
        _ => value?.ToString()
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ResultWindowModeConverter : IValueConverter
{
    public static readonly ResultWindowModeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ResultWindowMode.Classic => Resources.ResultWindowMode_Classic,
        ResultWindowMode.Dictionary => Resources.ResultWindowMode_Dictionary,
        _ => value?.ToString()
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ResultWindowModeToBooleanConverter : IValueConverter
{
    public static readonly ResultWindowModeToBooleanConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ResultWindowMode.Classic;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ScreenshotModeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        "Quick" => Resources.ScreenshotMode_Quick,
        "Precise" => Resources.ScreenshotMode_Precise,
        _ => value
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class OcrRecognitionModeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        OcrRecognitionMode.Fast => Resources.OcrRecognitionMode_Fast,
        OcrRecognitionMode.Normal => Resources.OcrRecognitionMode_Normal,
        OcrRecognitionMode.IdleRelease => Resources.OcrRecognitionMode_IdleRelease,
        _ => value?.ToString()
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class OcrRecognitionModeDescriptionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        OcrRecognitionMode.Fast => Resources.OcrRecognitionModeDescription_Fast,
        OcrRecognitionMode.Normal => Resources.OcrRecognitionModeDescription_Normal,
        OcrRecognitionMode.IdleRelease => Resources.OcrRecognitionModeDescription_IdleRelease,
        _ => null
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InputTranslationModeToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is InputTranslationMode mode
            ? mode switch
            {
                InputTranslationMode.Tsf => Resources.InputTranslationMode_Tsf,
                _ => Resources.InputTranslationMode_NormalWindow
            }
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InputTranslationModeToBoolConverter : IValueConverter
{
    public static readonly InputTranslationModeToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is InputTranslationMode.Tsf;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ImageTextEraseModeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ImageTextEraseMode.Fast => Get("ImageTextEraseMode_Fast", "Normal"),
        ImageTextEraseMode.Precise => Get("ImageTextEraseMode_Precise", "Precise (AOT-GAN)"),
        _ => value?.ToString()
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string Get(string key, string fallback) =>
        Resources.ResourceManager.GetString(key, Resources.Culture) ?? fallback;
}

public sealed class ImageTextEraseModeDescriptionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ImageTextEraseMode.Fast => Get(
            "ImageTextEraseModeDescription_Fast",
            "Uses adaptive background removal. No model download is required."),
        ImageTextEraseMode.Precise => Get(
            "ImageTextEraseModeDescription_Precise",
            "Uses AOT-GAN for background removal during image translation text replacement. Download the model before using precise mode."),
        _ => null
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string Get(string key, string fallback) =>
        Resources.ResourceManager.GetString(key, Resources.Culture) ?? fallback;
}
