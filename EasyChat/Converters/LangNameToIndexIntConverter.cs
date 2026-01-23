using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace EasyChat.Converters;

public class LangNameToIndexIntConverters
{
    // For Combobox use, involving English and Simplified Chinese, fetching corresponding index
    public static readonly LangNameToIndexIntConverter Lang = new();
}

public class LangNameToIndexIntConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s) return null;
        return s switch
        {
            "English" => 0,
            "Simplified Chinese" => 1,
            _ => 0
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int i) return null;
        return i switch
        {
            0 => "English",
            1 => "Simplified Chinese",
            _ => "English"
        };
    }
}