using System;
using System.Reactive;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using EasyChat.Constants;
using EasyChat.Lang;
using EasyChat.Models;
using EasyChat.Models.Configuration;
using EasyChat.Services.Abstractions;
using EasyChat.Services.ScreenCapture;
using ReactiveUI;
using SukiUI.Dialogs;

namespace EasyChat.ViewModels.Dialogs;

public class FixedAreaFormDialogViewModel : ViewModelBase
{
    private readonly ISukiDialog _dialog; // Connects to the dialog instance showing this VM
    private readonly ISukiDialogManager _dialogManager;
    private readonly IScreenCaptureService _screenCaptureService;
    private readonly FixedArea _originalArea;
    private readonly Action _onFinished; // Callback to parent to restore parent dialog

    private string _name;
    private int _x;
    private int _y;
    private int _width;
    private int _height;

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }
    
    // Read-only display of current area
    public int X
    {
        get => _x;
        set => this.RaiseAndSetIfChanged(ref _x, value);
    }
    public int Y
    {
        get => _y;
        set => this.RaiseAndSetIfChanged(ref _y, value);
    }
    public int Width
    {
        get => _width;
        set => this.RaiseAndSetIfChanged(ref _width, value);
    }
    public int Height
    {
        get => _height;
        set => this.RaiseAndSetIfChanged(ref _height, value);
    }

    public string DisplayInfo => $"X: {X}, Y: {Y}, W: {Width}, H: {Height}";

    public ReactiveCommand<Unit, Unit> ReselectAreaCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public FixedAreaFormDialogViewModel(
        ISukiDialog dialog, 
        ISukiDialogManager dialogManager,
        IScreenCaptureService screenCaptureService,
        FixedArea area,
        Action onFinished)
    {
        _dialog = dialog;
        _dialogManager = dialogManager;
        _screenCaptureService = screenCaptureService;
        _originalArea = area;
        _onFinished = onFinished;

        _name = area.Name;
        _x = area.X;
        _y = area.Y;
        _width = area.Width;
        _height = area.Height;

        ReselectAreaCommand = ReactiveCommand.Create(ReselectArea);
        ConfirmCommand = ReactiveCommand.Create(Confirm);
        CancelCommand = ReactiveCommand.Create(Cancel);
    }

    private void ReselectArea()
    {
        // 1. Close current dialog
        _dialog.Dismiss();

        // 2. Minimize window and Start Capture
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = desktop.MainWindow;
            if (mainWindow != null)
            {
                mainWindow.WindowState = Avalonia.Controls.WindowState.Minimized;

                // Wait for minimize
                Dispatcher.UIThread.Post(async () => {
                    await System.Threading.Tasks.Task.Delay(300);
                    
                    var session = new ScreenSelectionSession(_screenCaptureService,
                        (bitmap, intent) => { },
                        () => {
                            // Cancelled
                            Dispatcher.UIThread.Post(() => {
                                RestoreWindow(mainWindow);
                                ReopenSelf(); // Reopen without changes
                            });
                        },
                        Constant.ScreenshotMode.Precise,
                        (rect) => {
                            // Selected
                            Dispatcher.UIThread.Post(() => {
                                RestoreWindow(mainWindow);
                                
                                // Update values
                                X = rect.X;
                                Y = rect.Y;
                                Width = rect.Width;
                                Height = rect.Height;
                                this.RaisePropertyChanged(nameof(DisplayInfo));
                                
                                ReopenSelf(); // Reopen with new values
                            });
                        },
                        CaptureIntent.RectSelection
                    );
                    
                    session.Start();
                });
            }
        }
    }

    private void RestoreWindow(Avalonia.Controls.Window window)
    {
        window.WindowState = Avalonia.Controls.WindowState.Normal;
        window.Activate();
    }

    private void ReopenSelf()
    {
        // Re-create proper dialog with current state
        _dialogManager.CreateDialog()
            .WithTitle(Resources.Edit) // Or "Edit Area"
            .WithViewModel(d => new FixedAreaFormDialogViewModel(
                d, 
                _dialogManager, 
                _screenCaptureService, 
                _originalArea, 
                _onFinished)
            {
                // Initialize with current instance values (carried over from reselection)
                Name = this.Name,
                X = this.X,
                Y = this.Y,
                Width = this.Width,
                Height = this.Height
            })
            .TryShow();
    }

    private void Confirm()
    {
        // Update original area
        _originalArea.Name = Name;
        _originalArea.X = X;
        _originalArea.Y = Y;
        _originalArea.Width = Width;
        _originalArea.Height = Height;
        
        _dialog.Dismiss();
        _onFinished?.Invoke(); // Trigger parent restore
    }

    private void Cancel()
    {
        _dialog.Dismiss();
        _onFinished?.Invoke(); // Trigger parent restore
    }
}
