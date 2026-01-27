using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace EasyChat.Converters;

public class SubtitleVisibilityConverter : IMultiValueConverter
{
    public static readonly SubtitleVisibilityConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // Logic should match SubtitleContentConverter but return bool
        
        // Expected inputs:
        // [0]: OriginalText (string)
        // [1]: TranslatedText (string)
        // [2]: SourceType (int)
        
        if (values.Count < 3) return false;
        
        var original = values[0] as string;
        var translated = values[1] as string;
        var sourceRaw = values[2];

        int sourceType = 0;
        if (sourceRaw is int i) sourceType = i;

        var mode = parameter as string ?? "Secondary";
        
        string resultText = string.Empty;

        if (mode == "Main")
        {
            // Main: 0=Original, 1=Translated
            resultText = sourceType switch
            {
                0 => original,
                1 => translated,
                _ => string.Empty
            };
        }
        else 
        {
            // Secondary: 0=None, 1=Original, 2=Translated
             resultText = sourceType switch
            {
                0 => string.Empty, 
                1 => original,
                2 => translated,
                _ => string.Empty
            };
        }

        return !string.IsNullOrEmpty(resultText);
    }
}
