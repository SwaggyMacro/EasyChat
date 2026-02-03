using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace EasyChat.Views.Pages;

public partial class SpeechRecognitionView : UserControl
{
    private bool _isLoaded;

    public SpeechRecognitionView()
    {
        InitializeComponent();
        
        // Subscribe to Loaded event to trigger lazy loading
        Loaded += OnLoaded;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
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
            var mainContent = this.FindControl<Grid>("MainContent");
            var loadingOverlay = this.FindControl<Grid>("LoadingOverlay");
            
            if (mainContent != null) mainContent.IsVisible = true;
            if (loadingOverlay != null) loadingOverlay.IsVisible = false;
        });
    }
}
