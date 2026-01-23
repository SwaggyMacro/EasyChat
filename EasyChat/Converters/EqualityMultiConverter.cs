using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace EasyChat.Converters
{
    public class EqualityMultiConverter : IMultiValueConverter
    {
        public static readonly EqualityMultiConverter Instance = new();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values == null || values.Count < 2)
                return false;

            var val1 = values[0];
            var val2 = values[1];

            if (val1 == null && val2 == null)
                return true;

            if (val1 == null || val2 == null)
                return false;

            return val1.Equals(val2);
        }
    }
}
