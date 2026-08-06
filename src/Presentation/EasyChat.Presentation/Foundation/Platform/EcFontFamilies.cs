using Avalonia.Media;

namespace EasyChat.Presentation.Foundation.Platform;

/// <summary>
/// Cross-platform font stacks. Inter is registered via <c>WithInterFont()</c> on the host;
/// CJK and system UI faces follow so Windows / macOS / Linux stay readable without per-OS branches.
/// </summary>
public static class EcFontFamilies
{
    public const string UiStack =
        "Inter, Microsoft YaHei UI, Microsoft YaHei, PingFang SC, Hiragino Sans GB, Noto Sans CJK SC, Noto Sans SC, Segoe UI, sans-serif";

    public const string MonoStack =
        "Cascadia Mono, Cascadia Code, Consolas, Menlo, Monaco, Liberation Mono, monospace";

    public static FontFamily Ui { get; } = new(UiStack);

    public static FontFamily Mono { get; } = new(MonoStack);

    /// <summary>
    /// User-picked face first, then the Ec UI stack so a missing family never blanks glyphs.
    /// </summary>
    public static FontFamily Resolve(string? preferred)
    {
        if (string.IsNullOrWhiteSpace(preferred))
            return Ui;

        var trimmed = preferred.Trim();
        // Already a multi-family stack from settings history — keep as-is.
        if (trimmed.Contains(',', StringComparison.Ordinal))
        {
            try
            {
                return new FontFamily(trimmed);
            }
            catch (ArgumentException)
            {
                return Ui;
            }
        }

        try
        {
            return new FontFamily($"{trimmed}, {UiStack}");
        }
        catch (ArgumentException)
        {
            return Ui;
        }
    }
}
