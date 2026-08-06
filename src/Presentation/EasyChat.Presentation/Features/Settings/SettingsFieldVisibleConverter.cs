using System.Globalization;
using Avalonia.Data.Converters;

namespace EasyChat.Presentation.Features.Settings;

/// <summary>
/// Binds SearchText → visibility. ConverterParameter = field keyword bag.
/// </summary>
public sealed class SettingsFieldVisibleConverter : IValueConverter
{
    public static SettingsFieldVisibleConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var query = value as string;
        var keys = parameter as string ?? string.Empty;
        return SettingsSearch.Matches(query, keys);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
