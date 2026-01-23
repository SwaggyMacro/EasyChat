using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace EasyChat.Converters;

/// <summary>
///     Static converters for language flag icons.
/// </summary>
public static class LanguageFlagConverters
{
    public static readonly IValueConverter ToIcon = new LanguageFlagToIconConverter();
}

/// <summary>
///     Converts language icon filename (from LanguageDefinition.Icon) to Bitmap for Image Source binding.
/// </summary>
public class LanguageFlagToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string iconFileName && !string.IsNullOrEmpty(iconFileName))
        {
            var iconPath = $"avares://EasyChat/Assets/Images/Flags/mini/{iconFileName}";
            
            try
            {
                var uri = new Uri(iconPath);
                using var stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
            }
            catch
            {
                // Icon not found, return null
                return null;
            }
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
