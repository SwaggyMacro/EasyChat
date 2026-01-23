using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace EasyChat.Converters;

/// <summary>
/// Converts a boolean value to a color (green for true, gray for false).
/// Used for proxy status icons.
/// </summary>
public class BoolToColorConverter : IMultiValueConverter
{
    public static readonly BoolToColorConverter Instance = new();
    
    private static readonly SolidColorBrush GreenBrush = new(Color.Parse("#4CAF50"));
    private static readonly SolidColorBrush GrayBrush = new(Color.Parse("#9E9E9E"));

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count > 0 && values[0] is bool isEnabled)
        {
            return isEnabled ? GreenBrush : GrayBrush;
        }
        return GrayBrush;
    }
}
