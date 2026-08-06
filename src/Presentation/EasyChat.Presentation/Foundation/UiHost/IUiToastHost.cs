namespace EasyChat.Presentation.Foundation.UiHost;

/// <summary>
/// Thin toast host. Keeps progress/action toasts expressible without leaking
/// Suki toast builders into feature code.
/// </summary>
public interface IUiToastHost
{
    void Show(
        string title,
        string? content = null,
        UiMessageSeverity severity = UiMessageSeverity.Information,
        TimeSpan? autoDismiss = null);

    /// <summary>Sticky toast (e.g. download progress). Caller must <see cref="IUiToastSession.Dismiss"/>.</summary>
    IUiToastSession ShowSticky(string title, object? content = null);

    void ShowWithActions(string title, string content, params UiToastAction[] actions);
}

public interface IUiToastSession
{
    void Dismiss();
}

public sealed record UiToastAction(string Text, Action OnClick, bool DismissOnClick = true);
