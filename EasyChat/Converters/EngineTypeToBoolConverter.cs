using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using EasyChat.Constants;

namespace EasyChat.Converters;

public static class EngineTypeToBoolConverters
{
    public static readonly EngineTypeToBoolConverter AiModel = new(Constant.TransEngineType.Ai);
    public static readonly EngineTypeToBoolConverter MachineTrans = new(Constant.TransEngineType.Machine);
}

public class EngineTypeToBoolConverter : IValueConverter
{
    private readonly string _type;

    public EngineTypeToBoolConverter(string type)
    {
        _type = type;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s) return null;
        return s == _type;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool b) return BindingOperations.DoNothing;
        // Only update binding source when this RadioButton is checked (b == true).
        // When unchecked (b == false), return DoNothing to prevent overwriting with null.
        return b ? _type : BindingOperations.DoNothing;
    }
}