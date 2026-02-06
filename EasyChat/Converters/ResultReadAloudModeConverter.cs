using System;
using System.Globalization;
using Avalonia.Data.Converters;
using EasyChat.Lang;
using EasyChat.Models.Configuration;

namespace EasyChat.Converters;

public class ResultReadAloudModeConverter : IValueConverter
{
    public static readonly ResultReadAloudModeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ResultReadAloudMode mode)
        {
            return mode switch
            {
                ResultReadAloudMode.None => Resources.ReadAloudMode_None,
                ResultReadAloudMode.Source => Resources.ReadAloudMode_Source,
                ResultReadAloudMode.Target => Resources.ReadAloudMode_Target,
                ResultReadAloudMode.Both => Resources.ReadAloudMode_Both,
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
