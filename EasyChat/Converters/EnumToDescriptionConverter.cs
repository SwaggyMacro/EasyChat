using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Data.Converters;
using EasyChat.Lang;
using EasyChat.Models.Configuration;

namespace EasyChat.Converters;

public class EnumToDescriptionConverter : IValueConverter
{
    public static readonly EnumToDescriptionConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Enum enumValue) return value?.ToString();

        // Special handling for localized descriptions
        if (value is WindowClosingBehavior behavior)
        {
            return behavior switch
            {
                WindowClosingBehavior.Ask => Resources.Option_Ask,
                WindowClosingBehavior.ExitApp => Resources.Option_Exit,
                WindowClosingBehavior.MinimizeToTray => Resources.Option_Minimize,
                _ => value.ToString()
            };
        }

        var fieldInfo = value.GetType().GetField(value.ToString() ?? string.Empty);
        var attributes = (DescriptionAttribute[]?)fieldInfo?.GetCustomAttributes(typeof(DescriptionAttribute), false);

        return attributes?.Length > 0 ? attributes[0].Description : value.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
