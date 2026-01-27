using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EasyChat.Converters;

public class StringToFontFamilyConverter : IValueConverter
{
    public static readonly StringToFontFamilyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string fontName && !string.IsNullOrEmpty(fontName))
        {
            return new FontFamily(fontName);
        }
        return FontFamily.Default;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is FontFamily fontFamily)
        {
            return fontFamily.Name;
        }
        return string.Empty;
    }
}
