using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace EasyChat.Converters;

public class BoolToTextConverter : IValueConverter
{
    public static readonly BoolToTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool b) return null;

        string trueText = "True";
        string falseText = "False";

        if (parameter is string paramStr && paramStr.Contains('|'))
        {
            var parts = paramStr.Split('|');
            if (parts.Length >= 2)
            {
                var falseKey = parts[0];
                var trueKey = parts[1];

                // Try to get from resources, fallback to original string
                var falseRes = Lang.Resources.ResourceManager.GetString(falseKey, culture) ?? falseKey;
                var trueRes = Lang.Resources.ResourceManager.GetString(trueKey, culture) ?? trueKey;

                trueText = trueRes;
                falseText = falseRes;
            }
        }

        return b ? trueText : falseText;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
