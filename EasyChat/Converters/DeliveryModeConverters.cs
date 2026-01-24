using System;
using System.Globalization;
using Avalonia.Data.Converters;
using EasyChat.Lang;
using EasyChat.Models.Configuration;

namespace EasyChat.Converters;

public class DeliveryModeToBoolConverter : IValueConverter
{
    public static readonly DeliveryModeToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is InputDeliveryMode mode)
        {
            // Only Type and Message need delay
            return mode == InputDeliveryMode.Type || mode == InputDeliveryMode.Message;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class DeliveryModeToStringConverter : IValueConverter
{
    public static readonly DeliveryModeToStringConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is InputDeliveryMode mode)
        {
            return mode switch
            {
                InputDeliveryMode.Type => Resources.DeliveryMode_Type,
                InputDeliveryMode.Paste => Resources.DeliveryMode_Paste,
                InputDeliveryMode.Message => Resources.DeliveryMode_Message,
                _ => mode.ToString()
            };
        }
        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
