using EasyChat.Contracts.Updates;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Features.Shell;
using EasyChat.Presentation.Foundation.UiHost;

namespace EasyChat.Desktop;

public sealed record DesktopUiContext(
    SettingsSession Settings,
    MainWindowViewModel MainWindowViewModel,
    IUiDialogHost Dialogs,
    DesktopInteractionLifecycle Interactions,
    IApplicationUpdateService Updates,
    IUiToastHost Toasts);
