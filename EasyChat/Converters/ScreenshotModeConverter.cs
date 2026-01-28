using System;
using System.Globalization;
using Avalonia.Data.Converters;
using EasyChat.Constants;
using EasyChat.Lang;

namespace EasyChat.Converters;

public class ScreenshotModeConverter : IValueConverter
{
    public static readonly ScreenshotModeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string mode)
        {
            return mode switch
            {
                Constant.ScreenshotMode.Quick => Resources.ScreenshotMode_Quick,
                Constant.ScreenshotMode.Precise => Resources.ScreenshotMode_Precise,
                _ => mode
            };
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
