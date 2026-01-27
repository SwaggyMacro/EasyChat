using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace EasyChat.Converters;

public class SubtitleContentConverter : IMultiValueConverter
{
    public static readonly SubtitleContentConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // Expected inputs:
        // [0]: OriginalText (string)
        // [1]: TranslatedText (string)
        // [2]: SourceType (int)
        
        if (values.Count < 3) return string.Empty;
        
        var original = values[0] as string ?? string.Empty;
        var translated = values[1] as string ?? string.Empty;
        var sourceRaw = values[2];

        // Handle potential UnsetValue or Type issues for source
        int sourceType = 0;
        if (sourceRaw is int i) sourceType = i;

        var mode = parameter as string ?? "Secondary";

        if (mode == "Main")
        {
            // Main: 0=Original, 1=Translated
            return sourceType switch
            {
                0 => original,
                1 => translated,
                _ => string.Empty
            };
        }
        else 
        {
            // Secondary: 0=None, 1=Original, 2=Translated
             return sourceType switch
            {
                0 => string.Empty, 
                1 => original,
                2 => translated,
                _ => string.Empty
            };
        }
    }
}
