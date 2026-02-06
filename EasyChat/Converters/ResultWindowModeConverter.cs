using System;
using System.Globalization;
using Avalonia.Data.Converters;
using EasyChat.Lang;
using EasyChat.Models.Configuration;

namespace EasyChat.Converters;

public class ResultWindowModeConverter : IValueConverter
{
    public static readonly ResultWindowModeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ResultWindowMode mode)
        {
            return mode switch
            {
                ResultWindowMode.Classic => Resources.ResultWindowMode_Classic,
                ResultWindowMode.Dictionary => Resources.ResultWindowMode_Dictionary,
                _ => mode.ToString()
            };
        }
        return value;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
