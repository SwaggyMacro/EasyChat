using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;

namespace EasyChat.Converters;

public static class BoolToIconConverters
{
    public static readonly BoolToIconConverter Animation = new(MaterialIconKind.Pause, MaterialIconKind.Play);
    public static readonly BoolToIconConverter WindowLock = new(MaterialIconKind.Unlocked, MaterialIconKind.Lock);
    public static readonly BoolToIconConverter Visibility = new(MaterialIconKind.EyeClosed, MaterialIconKind.Eye);
    public static readonly BoolToIconConverter Simple = new(MaterialIconKind.Close, MaterialIconKind.Ticket);
}

public class BoolToIconConverter(MaterialIconKind trueIcon = MaterialIconKind.Help, MaterialIconKind falseIcon = MaterialIconKind.Help) : IValueConverter
{
    // Singleton instance for XAML usage with ConverterParameter
    public static readonly BoolToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool b) return null;

        MaterialIconKind tIcon = trueIcon;
        MaterialIconKind fIcon = falseIcon;

        // If ConverterParameter is provided as "TrueIcon;FalseIcon", parse it
        if (parameter is string paramStr && paramStr.Contains(';'))
        {
            var parts = paramStr.Split(';');
            if (parts.Length >= 2)
            {
                Enum.TryParse(parts[0], true, out tIcon);
                Enum.TryParse(parts[1], true, out fIcon);
            }
        }

        return b ? tIcon : fIcon;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}