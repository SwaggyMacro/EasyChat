using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace EasyChat.Converters;

/// <summary>
///     Static converters for engine name to icon.
/// </summary>
public static class EngineConverters
{
    public static readonly IValueConverter ToIcon = new EngineNameToIconConverter();
    public static readonly IValueConverter HasIcon = new EngineHasIconConverter();
    public static readonly IValueConverter HasNoIcon = new EngineHasNoIconConverter();
}

/// <summary>
///     Converts engine name (string) to corresponding icon Bitmap for Image Source binding.
///     Supports both Machine Translation engines and AI model types.
/// </summary>
public class EngineNameToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string engineName && !string.IsNullOrEmpty(engineName))
        {
            // Map engine names to icon file names
            var iconFileName = engineName.ToLowerInvariant() switch
            {
                "baidu" => "Baidu.png",
                "tencent" => "Tencent.png",
                "google" => "Google.png",
                "deepl" => "DeepL.png",
                "bing" => "Bing.png",
                "youdao" => "Youdao.png",
                // AI Models (check if the name contains the model type)
                _ when engineName.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) => "openai.png",
                _ when engineName.Contains("Gemini", StringComparison.OrdinalIgnoreCase) => "gemini.png",
                _ when engineName.Contains("Claude", StringComparison.OrdinalIgnoreCase) => "claude.png",
                // Fallback: try using the engine name directly
                _ => $"{engineName}.png"
            };

            var iconPath = $"avares://EasyChat/Assets/Images/Engine/{iconFileName}";
            
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

    /// <summary>
    ///     Shared helper method to check if an icon exists for the given engine name.
    /// </summary>
    public static bool IconExists(string? engineName)
    {
        if (string.IsNullOrEmpty(engineName)) return false;
        
        var iconFileName = engineName.ToLowerInvariant() switch
        {
            "baidu" => "Baidu.png",
            "tencent" => "Tencent.png",
            "google" => "Google.png",
            "deepl" => "DeepL.png",
            "bing" => "Bing.png",
            "youdao" => "Youdao.png",
            _ when engineName.Contains("OpenAI", StringComparison.OrdinalIgnoreCase) => "openai.png",
            _ when engineName.Contains("Gemini", StringComparison.OrdinalIgnoreCase) => "gemini.png",
            _ when engineName.Contains("Claude", StringComparison.OrdinalIgnoreCase) => "claude.png",
            _ => null // No icon for custom models
        };

        if (iconFileName == null) return false;

        try
        {
            var iconPath = $"avares://EasyChat/Assets/Images/Engine/{iconFileName}";
            var uri = new Uri(iconPath);
            using var stream = AssetLoader.Open(uri);
            return stream != null;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
///     Returns true if an icon exists for the engine name.
/// </summary>
public class EngineHasIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return EngineNameToIconConverter.IconExists(value as string);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
///     Returns true if NO icon exists for the engine name (for showing fallback).
/// </summary>
public class EngineHasNoIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !EngineNameToIconConverter.IconExists(value as string);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
