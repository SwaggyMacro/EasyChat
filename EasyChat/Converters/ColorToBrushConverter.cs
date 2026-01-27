using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EasyChat.Converters;

public class ColorToBrushConverter : IValueConverter
{
    public static readonly ColorToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            if (Color.TryParse(hex, out var color))
            {
                return new SolidColorBrush(color);
            }
        }
        else if (value is Color c)
        {
            return new SolidColorBrush(c);
        }
        
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SolidColorBrush brush)
        {
             return brush.Color.ToString();
        }
        return "#00000000";
    }
}
