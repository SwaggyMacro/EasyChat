using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace EasyChat.Views.Pages;

public partial class SettingView : UserControl
{
    private bool _isLoaded;
    
    public SettingView()
    {
        InitializeComponent();
        
        // Subscribe to Loaded event to trigger lazy loading
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Only load once
        if (_isLoaded) return;
        _isLoaded = true;
        
        // Delay to allow the loading indicator to render and be visible
        await Task.Delay(200);
        
        // Switch visibility on UI thread
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            SettingsContent.IsVisible = true;
            LoadingOverlay.IsVisible = false;
        });
    }
}