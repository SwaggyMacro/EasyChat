using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace EasyChat.Converters;

public class SubtitleLoadingConverter : IMultiValueConverter
{
    public static readonly SubtitleLoadingConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // Expected inputs:
        // [0]: IsTranslating (bool)
        // [1]: SourceType (int)
        
        if (values.Count < 2) return false;
        
        var isTranslating = values[0] is bool b && b;
        var sourceRaw = values[1];
        
        int sourceType = 0;
        if (sourceRaw is int i) sourceType = i;

        var mode = parameter as string ?? "Secondary";

        // Logic: Show loading ONLY if we are translating AND this line is showing the Translation.
        // Main: 0=Original, 1=Translated
        // Secondary: 0=None, 1=Original, 2=Translated
        
        if (mode == "Main")
        {
            // If Main is showing Translated (1) and we are translating
            return isTranslating && sourceType == 1;
        }
        else
        {
            // If Secondary is showing Translated (2) and we are translating
            return isTranslating && sourceType == 2;
        }
    }
}
