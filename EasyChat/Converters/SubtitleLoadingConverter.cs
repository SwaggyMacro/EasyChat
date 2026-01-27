using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

using EasyChat.Models.Configuration;

namespace EasyChat.Converters;

public class SubtitleLoadingConverter : IMultiValueConverter
{
    public static readonly SubtitleLoadingConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // Expected inputs:
        // [0]: IsTranslating (bool)
        // [1]: SourceType (SubtitleSource)
        
        if (values.Count < 2) return false;
        
        var isTranslating = values[0] is bool b && b;
        var sourceRaw = values[1];
        
        SubtitleSource sourceType = SubtitleSource.None;
        if (sourceRaw is SubtitleSource s) sourceType = s;
        else if (sourceRaw is int i) sourceType = (SubtitleSource)i;

        var mode = parameter as string ?? "Secondary";

        // Logic: Show loading ONLY if we are translating AND this line is showing the Translation.
        // Main: 0=Original, 1=Translated (OLD) -> Now Enum
        // Secondary: 0=None, 1=Original, 2=Translated
        
        if (mode == "Main")
        {
            // If Main is showing Translated and we are translating
            return isTranslating && sourceType == SubtitleSource.Translated;
        }
        else
        {
            // If Secondary is showing Translated and we are translating
            return isTranslating && sourceType == SubtitleSource.Translated;
        }
    }
}
