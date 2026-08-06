using System.Globalization;
using System.Reflection;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace EasyChat.Presentation.Shared.Controls;

public static class LanguageFlagConverters
{
    public static readonly IValueConverter ToIcon = new LanguageFlagToIconConverter();
    public static readonly IValueConverter HasIcon = new LanguageFlagHasIconConverter();
    public static readonly IValueConverter HasNoIcon = new LanguageFlagHasNoIconConverter();
}

public sealed class LanguageFlagToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        LanguageFlagAssetLoader.Load(value as string);

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

internal static class LanguageFlagAssetLoader
{
    private const string AssetRoot = "avares://EasyChat.Desktop/Assets/Images/Flags/mini/";

    public static Bitmap? Load(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
            return null;

        try
        {
            using var stream = AssetLoader.Open(new Uri($"{AssetRoot}{file}"));
            return new Bitmap(stream);
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

/// <summary>
/// Resolves a language item's display text for the current UI culture: picks
/// <c>ChineseName</c>/<c>EnglishName</c> (Chinese UI shows Chinese names, any
/// other UI shows English names) and falls back to <c>DisplayName</c> for item
/// types that do not expose the two names separately. Works with any language
/// item type via reflection, so it can be used inside generic controls.
/// </summary>
public sealed class LanguageDisplayNameConverter : IValueConverter
{
    public static readonly LanguageDisplayNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return null;

        var type = value.GetType();
        var chineseName = GetStringProperty(type, value, "ChineseName");
        var englishName = GetStringProperty(type, value, "EnglishName");
        if (chineseName is not null || englishName is not null)
            return ForUi(chineseName, englishName ?? string.Empty);

        return GetStringProperty(type, value, "DisplayName");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string? GetStringProperty(Type type, object instance, string name) =>
        type.GetProperty(name)?.GetValue(instance) as string;

    private static string ForUi(string? chineseName, string englishName)
    {
        var culture = CultureInfo.CurrentUICulture;
        return culture.TwoLetterISOLanguageName == "zh" && !string.IsNullOrWhiteSpace(chineseName)
            ? chineseName
            : englishName;
    }
}
