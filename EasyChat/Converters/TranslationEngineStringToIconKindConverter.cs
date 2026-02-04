using System;
using System.Globalization;
using Avalonia.Data.Converters;
using EasyChat.Lang;
using Material.Icons;

namespace EasyChat.Converters;

public class TranslationEngineStringToIconKindConverter : IValueConverter
{
    public static readonly TranslationEngineStringToIconKindConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            if (s == Resources.AIEngine) return MaterialIconKind.Robot;
            if (s == Resources.MachineTranslation) return MaterialIconKind.Translate;
        }
        return MaterialIconKind.HelpCircleOutline;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
