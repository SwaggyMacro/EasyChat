using System;
using System.Globalization;
using Avalonia.Data.Converters;
using EasyChat.Models.Configuration;

namespace EasyChat.Converters;

public class ResultWindowModeToBooleanConverter : IValueConverter
{
    public static readonly ResultWindowModeToBooleanConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ResultWindowMode mode)
        {
            // Returns true if mode is Classic, meaning the settings should be visible.
            return mode == ResultWindowMode.Classic;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
