using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

using EasyChat.Models.Configuration;

namespace EasyChat.Converters;

public class SubtitleContentConverter : IMultiValueConverter
{
    public static readonly SubtitleContentConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // Expected inputs:
        // [0]: OriginalText (string)
        // [1]: TranslatedText (string)
        // [2]: SourceType (SubtitleSource)
        
        if (values.Count < 3) return string.Empty;
        
        var original = values[0] as string ?? string.Empty;
        var translated = values[1] as string ?? string.Empty;
        var sourceRaw = values[2];

        SubtitleSource sourceType = SubtitleSource.None;
        if (sourceRaw is SubtitleSource s) sourceType = s;
        else if (sourceRaw is int i) sourceType = (SubtitleSource)i;

        var mode = parameter as string ?? "Secondary";

        if (mode == "Main")
        {
            // Main: Original or Translated
            return sourceType switch
            {
                SubtitleSource.Original => original,
                SubtitleSource.Translated => translated,
                _ => string.Empty
            };
        }
        else 
        {
            // Secondary: None, Original, Translated
             return sourceType switch
            {
                SubtitleSource.None => string.Empty, 
                SubtitleSource.Original => original,
                SubtitleSource.Translated => translated,
                _ => string.Empty
            };
        }
    }
}
