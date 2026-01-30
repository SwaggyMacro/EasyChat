using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using EasyChat.Constants;
using EasyChat.Lang;
using EasyChat.Models;
using EasyChat.Models.Configuration;
using EasyChat.Services.Abstractions;
using EasyChat.Services.ScreenCapture;
using ReactiveUI;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using Avalonia.Threading;

namespace EasyChat.ViewModels.Dialogs;

public class FixedAreaEditDialogViewModel : ViewModelBase
{
    private readonly ISukiDialogManager _dialogManager;
    private readonly IConfigurationService _configurationService;
    private readonly IScreenCaptureService _screenCaptureService;
    
    // We need to keep a reference to the dialog instance to close/hide it
    private readonly ISukiDialog _dialog; 
    
    public ObservableCollection<FixedArea> FixedAreas => _configurationService.Screenshot?.FixedAreas ?? new ObservableCollection<FixedArea>();

    public bool HasAreas => FixedAreas.Count > 0;

    public ReactiveCommand<Unit, Unit> AddAreaCommand { get; }
    public ReactiveCommand<FixedArea, Unit> DeleteAreaCommand { get; }
    public ReactiveCommand<FixedArea, Unit> EditAreaCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public FixedAreaEditDialogViewModel(ISukiDialogManager dialogManager, ISukiDialog dialog, IConfigurationService configurationService, IScreenCaptureService screenCaptureService)
    {
        _dialogManager = dialogManager;
        _dialog = dialog;
        _configurationService = configurationService;
        _screenCaptureService = screenCaptureService;

        AddAreaCommand = ReactiveCommand.Create(AddArea);
        DeleteAreaCommand = ReactiveCommand.Create<FixedArea>(DeleteArea);
        EditAreaCommand = ReactiveCommand.Create<FixedArea>(EditArea);
        CloseCommand = ReactiveCommand.Create(Close);
        
        // Monitor collection changes to update HasAreas
        FixedAreas.CollectionChanged += (s, e) => this.RaisePropertyChanged(nameof(HasAreas));
    }

    private void AddArea()
    {
        // 1. Hide the current dialog (and potentially the main window, but SukiUI dialogs are overlays)
        // Since we can't easily "hide" just the dialog without closing it in SukiUI (usually), 
        // we might need to close it and re-open it after selection, 
        // OR minimize the main window which hides everything.
        
        // Let's try minimizing the main window.
         if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
         {
             var mainWindow = desktop.MainWindow;
             if (mainWindow != null)
             {
                 mainWindow.WindowState = Avalonia.Controls.WindowState.Minimized;
                 
                 // 2. Start Selection
                 // We need to wait a bit for minimize animation?
                 // Start session
                 var session = new ScreenSelectionSession(_screenCaptureService, 
                     (bitmap, intent) => {}, // Standard capture unused here
                     () => {
                         // On Cancel
                         Dispatcher.UIThread.Post(() => {
                             mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                             mainWindow.Activate();
                         });
                     },
                     Constant.ScreenshotMode.Precise, // Unused? Or use Precise to allow adjustment
                     (rect) => {
                         // On Rect Selected
                         Dispatcher.UIThread.Post(() => {
                             mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                             mainWindow.Activate();
                             
                             // Add the new area
                             var newArea = new FixedArea
                             {
                                 Name = $"Area {FixedAreas.Count + 1}",
                                 X = rect.X,
                                 Y = rect.Y,
                                 Width = rect.Width,
                                 Height = rect.Height
                             };
                             FixedAreas.Add(newArea);
                             // _configurationService.Save(); // Handled by service
                         });
                     },
                     CaptureIntent.RectSelection
                 );
                 
                 // Delay slightly to allow minimize?
                 Dispatcher.UIThread.Post(async () => {
                     await System.Threading.Tasks.Task.Delay(300);
                     session.Start();
                 });
             }
         }
    }

    private void DeleteArea(FixedArea area)
    {
        FixedAreas.Remove(area);
        // _configurationService.Save(); // Handled by service
    }

    private void EditArea(FixedArea area)
    {
         // 1. Dismiss this dialog
         _dialog.Dismiss();
         
         // 2. Open Form Dialog
         _dialogManager.CreateDialog()
            .WithTitle(Resources.Edit)
            .WithViewModel(d => 
                new FixedAreaFormDialogViewModel(
                    d, 
                    _dialogManager, 
                    _screenCaptureService, 
                    area, 
                    () => {
                        // On Finished (Save or Cancel), reopen the list dialog
                        Dispatcher.UIThread.Post(() => {
                             _dialogManager.CreateDialog()
                                .WithTitle(Resources.FixedAreas)
                                .WithViewModel(newDialog => new FixedAreaEditDialogViewModel(
                                    _dialogManager, 
                                    newDialog, 
                                    _configurationService, 
                                    _screenCaptureService))
                                .TryShow();
                        });
                    }))
            .TryShow();
    }
    
    private void Close()
    {
        _dialog.Dismiss();
    }
}
