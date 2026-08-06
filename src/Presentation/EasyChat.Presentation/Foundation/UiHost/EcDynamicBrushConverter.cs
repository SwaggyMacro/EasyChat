using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace EasyChat.Presentation.Foundation.UiHost;

/// <summary>
/// Resolves a DynamicResource brush key name (e.g. "EcDanger") to the live theme brush.
/// Severity color is chosen in code but must still track light/dark tokens.
/// </summary>
public sealed class EcDynamicBrushConverter : IValueConverter
{
    public static readonly EcDynamicBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string key && !string.IsNullOrWhiteSpace(key))
            return Resolve(key) ?? Resolve("EcTextSecondary");

        return Resolve("EcTextSecondary");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static IBrush? Resolve(string key)
    {
        var app = Application.Current;
        if (app is null)
            return null;

        ThemeVariant? theme = app.ActualThemeVariant;
        if (app.TryGetResource(key, theme, out var resource) && resource is IBrush brush)
            return brush;

        return null;
    }
}
