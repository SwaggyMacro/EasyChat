using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

using EasyChat.Models.Configuration;

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
        // [2]: SourceType (SubtitleSource)
        
        if (values.Count < 3) return false;
        
        var original = values[0] as string;
        var translated = values[1] as string;
        var sourceRaw = values[2];

        SubtitleSource sourceType = SubtitleSource.None;
        if (sourceRaw is SubtitleSource s) sourceType = s;
        else if (sourceRaw is int i) sourceType = (SubtitleSource)i;

        var mode = parameter as string ?? "Secondary";
        
        string? resultText;

        if (mode == "Main")
        {
            // Main: Original or Translated
            resultText = sourceType switch
            {
                SubtitleSource.Original => original,
                SubtitleSource.Translated => translated,
                _ => string.Empty
            };
        }
        else 
        {
            // Secondary: None, Original, Translated
             resultText = sourceType switch
            {
                SubtitleSource.None => string.Empty, 
                SubtitleSource.Original => original,
                SubtitleSource.Translated => translated,
                _ => string.Empty
            };
        }

        return !string.IsNullOrEmpty(resultText);
    }
}
