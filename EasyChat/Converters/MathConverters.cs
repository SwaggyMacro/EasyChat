using Avalonia.Data.Converters;

namespace EasyChat.Converters;

public class MathConverters
{
    public static readonly IValueConverter Multiply = new FuncValueConverter<double, object, double>((value, parameter) =>
    {
        if (parameter is double d) return value * d;
        if (parameter is string s && double.TryParse(s, out double sd)) return value * sd;
        return value;
    });
}
