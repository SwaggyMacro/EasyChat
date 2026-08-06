using SukiUI.Dialogs;

namespace EasyChat.Presentation.Foundation.UiHost;

/// <summary>Production dialog host backed by SukiUI. Only adapter that may reference <see cref="ISukiDialogManager"/>.</summary>
public sealed class SukiUiDialogHost(ISukiDialogManager dialogs) : IUiDialogHost
{
    private readonly ISukiDialogManager _dialogs = dialogs;

    public void ShowMessage(UiMessageDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // ViewModel-only + Ec SukiDialog theme: no floating badge / glass / 55px offset.
        var builder = _dialogs.CreateDialog();
        builder.Dialog.ShowCardBackground = false;
        builder = builder.WithViewModel(dialog =>
            new EcMessageDialogViewModel(new SukiDialogSession(dialog), options));

        if (options.DismissOnBackgroundClick)
            builder = builder.Dismiss().ByClickingBackground();

        builder.TryShow();
    }

    public void ShowContent(UiContentDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builder = _dialogs.CreateDialog();
        builder.Dialog.ShowCardBackground = false;
        if (!string.IsNullOrWhiteSpace(options.Title))
            builder = builder.WithTitle(options.Title);

        builder = builder.WithViewModel(dialog => options.CreateContent(new SukiDialogSession(dialog)));

        if (options.DismissOnBackgroundClick)
            builder = builder.Dismiss().ByClickingBackground();

        builder.TryShow();
    }

    private sealed class SukiDialogSession(ISukiDialog dialog) : IUiDialogSession
    {
        public void Dismiss() => dialog.Dismiss();
    }
}
