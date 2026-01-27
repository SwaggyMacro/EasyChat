using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using EasyChat.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using SukiUI.Dialogs;
using EasyChat.Views;
using System.Reactive;
using EasyChat.Common;

namespace EasyChat.ViewModels.Dialogs;

public class CloseBehaviorDialogViewModel : ViewModelBase
{
    private readonly ISukiDialog _dialog;

    public CloseBehaviorDialogViewModel(ISukiDialog dialog)
    {
        _dialog = dialog;
        MinimizeCommand = ReactiveCommand.Create(Minimize);
        ExitAppCommand = ReactiveCommand.Create(ExitApp);
    }

    public bool IsRemember
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<Unit, Unit> MinimizeCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitAppCommand { get; }

    private void Minimize()
    {
        var configService = Global.Services?.GetRequiredService<IConfigurationService>();
        
        // Use property to update state
        if (IsRemember)
        {
            configService?.General?.ClosingBehavior = Models.Configuration.WindowClosingBehavior.MinimizeToTray;
        }
        else
        {
            // If not remembering, we must manually ensure the tray icon is visible for this action
            if (Application.Current is App app)
            {
                app.ForceShowTrayIcon();
            }
        }

        if (Global.Services?.GetRequiredService<IApplicationLifetime>() is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: MainWindow mainWindow
            })
        {
            mainWindow.Hide();
        }
        
        _dialog.Dismiss();
    }

    private void ExitApp()
    {
        var configService = Global.Services?.GetRequiredService<IConfigurationService>();
        if (IsRemember)
        {
             configService?.General?.ClosingBehavior = Models.Configuration.WindowClosingBehavior.ExitApp;
        }

        if (Global.Services?.GetRequiredService<IApplicationLifetime>() is IClassicDesktopStyleApplicationLifetime
            {
                MainWindow: MainWindow mainWindow
            })
        {
            mainWindow.IsExiting = true;
            mainWindow.Close();
        }
        
        _dialog.Dismiss();
    }
}
