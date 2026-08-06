using Avalonia.Controls.Notifications;
using SukiUI.Toasts;

namespace EasyChat.Presentation.Foundation.UiHost;

/// <summary>Production toast host backed by SukiUI. Only adapter that may reference <see cref="ISukiToastManager"/>.</summary>
public sealed class SukiUiToastHost(ISukiToastManager toasts) : IUiToastHost
{
    private readonly ISukiToastManager _toasts = toasts;

    public void Show(
        string title,
        string? content = null,
        UiMessageSeverity severity = UiMessageSeverity.Information,
        TimeSpan? autoDismiss = null)
    {
        var builder = _toasts.CreateSimpleInfoToast()
            .OfType(Map(severity))
            .WithTitle(title);

        if (!string.IsNullOrWhiteSpace(content))
            builder = builder.WithContent(content);

        if (autoDismiss is { } delay)
            builder = builder.Dismiss().After(delay);

        builder.Queue();
    }

    public IUiToastSession ShowSticky(string title, object? content = null)
    {
        var builder = _toasts.CreateToast().WithTitle(title);
        if (content is not null)
            builder = builder.WithContent(content);

        var toast = builder.Queue();
        return new SukiToastSession(_toasts, toast);
    }

    public void ShowWithActions(string title, string content, params UiToastAction[] actions)
    {
        var builder = _toasts.CreateToast()
            .WithTitle(title)
            .WithContent(content);

        foreach (var action in actions)
        {
            builder = builder.WithActionButton(
                action.Text,
                _ => action.OnClick(),
                action.DismissOnClick);
        }

        builder.Queue();
    }

    private static NotificationType Map(UiMessageSeverity severity) => severity switch
    {
        UiMessageSeverity.Success => NotificationType.Success,
        UiMessageSeverity.Warning => NotificationType.Warning,
        UiMessageSeverity.Error => NotificationType.Error,
        _ => NotificationType.Information
    };

    private sealed class SukiToastSession(ISukiToastManager manager, ISukiToast toast) : IUiToastSession
    {
        public void Dismiss() => manager.Dismiss(toast);
    }
}
