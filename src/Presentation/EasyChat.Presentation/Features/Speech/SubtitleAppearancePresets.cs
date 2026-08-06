using EasyChat.Presentation.Lang;

namespace EasyChat.Presentation.Features.Speech;

/// <summary>One-click visual theme for the floating subtitle overlay.</summary>
public sealed record SubtitleAppearancePreset(
    string Id,
    string DisplayName,
    string Description,
    double PrimaryFontSize,
    string PrimaryFontColor,
    double SecondaryFontSize,
    string SecondaryFontColor,
    string BackgroundColor,
    string SubtitleBackgroundColor,
    double WindowOpacity);

public static class SubtitleAppearancePresets
{
    public const string ClassicDarkId = "classic-dark";
    public const string HighContrastId = "high-contrast";
    public const string SoftLightId = "soft-light";
    public const string CinemaId = "cinema";
    public const string NeonId = "neon";

    public static IReadOnlyList<SubtitleAppearancePreset> All { get; } =
    [
        new(
            ClassicDarkId,
            Resources.SubtitlePreset_ClassicDark,
            Resources.SubtitlePreset_ClassicDarkDesc,
            PrimaryFontSize: 20,
            PrimaryFontColor: "#FFFFFFFF",
            SecondaryFontSize: 16,
            SecondaryFontColor: "#FFCCCCCC",
            BackgroundColor: "#99000000",
            SubtitleBackgroundColor: "#00000000",
            WindowOpacity: 0.92),
        new(
            HighContrastId,
            Resources.SubtitlePreset_HighContrast,
            Resources.SubtitlePreset_HighContrastDesc,
            PrimaryFontSize: 22,
            PrimaryFontColor: "#FFFFFF00",
            SecondaryFontSize: 18,
            SecondaryFontColor: "#FFFFFFFF",
            BackgroundColor: "#E6000000",
            SubtitleBackgroundColor: "#CC000000",
            WindowOpacity: 1.0),
        new(
            SoftLightId,
            Resources.SubtitlePreset_SoftLight,
            Resources.SubtitlePreset_SoftLightDesc,
            PrimaryFontSize: 18,
            PrimaryFontColor: "#FF0F172A",
            SecondaryFontSize: 15,
            SecondaryFontColor: "#FF475569",
            BackgroundColor: "#E6F8FAFC",
            SubtitleBackgroundColor: "#B3FFFFFF",
            WindowOpacity: 0.96),
        new(
            CinemaId,
            Resources.SubtitlePreset_Cinema,
            Resources.SubtitlePreset_CinemaDesc,
            PrimaryFontSize: 22,
            PrimaryFontColor: "#FFFFF4E0",
            SecondaryFontSize: 16,
            SecondaryFontColor: "#FFD4B896",
            BackgroundColor: "#D9000000",
            SubtitleBackgroundColor: "#66000000",
            WindowOpacity: 0.9),
        new(
            NeonId,
            Resources.SubtitlePreset_Neon,
            Resources.SubtitlePreset_NeonDesc,
            PrimaryFontSize: 20,
            PrimaryFontColor: "#FF22D3EE",
            SecondaryFontSize: 16,
            SecondaryFontColor: "#FFF472B6",
            BackgroundColor: "#E00B1020",
            SubtitleBackgroundColor: "#66111A2E",
            WindowOpacity: 0.94)
    ];

    public static SubtitleAppearancePreset? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(preset =>
                string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase));
}
