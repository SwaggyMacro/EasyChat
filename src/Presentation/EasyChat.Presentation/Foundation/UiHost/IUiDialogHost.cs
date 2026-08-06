namespace EasyChat.Presentation.Foundation.UiHost;

/// <summary>
/// Thin dialog host. Feature code depends on this — not SukiUI / ShadUI managers —
/// so a full chrome migration can swap adapters without rewriting every call site.
/// </summary>
public interface IUiDialogHost
{
    void ShowMessage(UiMessageDialogOptions options);

    void ShowContent(UiContentDialogOptions options);
}

public sealed class UiMessageDialogOptions
{
    public required string Title { get; init; }
    public required string Message { get; init; }
    public UiMessageSeverity Severity { get; init; } = UiMessageSeverity.Information;
    public string? PrimaryText { get; init; }
    public Action? OnPrimary { get; init; }
    public bool PrimaryIsDanger { get; init; }
    public string? SecondaryText { get; init; }
    public Action? OnSecondary { get; init; }
    public bool DismissOnBackgroundClick { get; init; }
}

public sealed class UiContentDialogOptions
{
    public string? Title { get; init; }
    public required Func<IUiDialogSession, object> CreateContent { get; init; }
    public bool DismissOnBackgroundClick { get; init; }
}
