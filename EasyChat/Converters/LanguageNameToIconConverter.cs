using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace EasyChat.Converters;

/// <summary>
///     Converts language display name (e.g. "English") to corresponding flag icon Bitmap.
/// </summary>
public class LanguageNameToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string languageName && !string.IsNullOrEmpty(languageName))
        {
            var iconFileName = languageName switch
            {
                "English" => "us.png",
                "Simplified Chinese" => "cn.png",
                "Traditional Chinese" => "tw.png", // Assuming existence, fallback safe
                "Japanese" => "jp.png",
                "Korean" => "kr.png",
                "French" => "fr.png",
                "German" => "de.png",
                "Spanish" => "es.png",
                 _ => null
            };

            if (iconFileName == null) return null;

            var iconPath = $"avares://EasyChat/Assets/Images/Flags/mini/{iconFileName}";
            
            try
            {
                var uri = new Uri(iconPath);
                using var stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
            }
            catch
            {
                // Icon not found, return null
                return null;
            }
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
