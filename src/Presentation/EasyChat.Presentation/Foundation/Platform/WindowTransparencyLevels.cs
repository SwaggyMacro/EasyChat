using Avalonia.Controls;

namespace EasyChat.Presentation.Foundation.Platform;

/// <summary>
/// Builds <see cref="Window.TransparencyLevelHint"/> lists with platform fallbacks.
/// Avalonia picks the first level the compositor actually supports, so Acrylic → Blur → solid.
/// </summary>
public static class WindowTransparencyLevels
{
    public const string AcrylicBlur = "AcrylicBlur";
    public const string Blur = "Blur";
    public const string Transparent = "Transparent";
    public const string None = "None";

    public static IReadOnlyList<string> Preferences { get; } =
        [AcrylicBlur, Blur, Transparent, None];

    /// <summary>
    /// Preference string from settings → ordered hint list for graceful degradation.
    /// </summary>
    public static IReadOnlyList<WindowTransparencyLevel> ForPreference(string? preference)
    {
        if (string.Equals(preference, AcrylicBlur, StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.Transparent,
                WindowTransparencyLevel.None
            ];
        }

        if (string.Equals(preference, Blur, StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.Transparent,
                WindowTransparencyLevel.None
            ];
        }

        if (string.Equals(preference, None, StringComparison.OrdinalIgnoreCase))
            return [WindowTransparencyLevel.None];

        // Transparent (default) and unknown values
        return
        [
            WindowTransparencyLevel.Transparent,
            WindowTransparencyLevel.None
        ];
    }
}
