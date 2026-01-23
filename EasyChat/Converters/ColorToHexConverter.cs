using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EasyChat.Converters;

public class ColorToHexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex)) // String -> Color
        {
            if (Color.TryParse(hex, out var color))
            {
                return color;
            }
        }
        else if (value is Color col) // Color -> String
        {
             return col.ToString();
        }
        return value is Color ? value.ToString() : Colors.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Color color) // Color -> String
        {
            return color.ToString();
        }
        else if (value is string hex && !string.IsNullOrEmpty(hex)) // String -> Color
        {
            if (Color.TryParse(hex, out var col))
            {
                return col;
            }
        }
        return value is string ? Colors.Transparent : "#00000000";
    }
}
