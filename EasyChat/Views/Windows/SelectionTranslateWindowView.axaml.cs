using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using EasyChat.Common;
using EasyChat.ViewModels.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyChat.Views.Windows;

public partial class SelectionTranslateWindowView : Window
{
    private readonly SelectionTranslateWindowViewModel _viewModel;
    private readonly ILogger<SelectionTranslateWindowView>? _logger;
    
    public SelectionTranslateWindowView()
    {
        InitializeComponent();
        
        _viewModel = new SelectionTranslateWindowViewModel();
        DataContext = _viewModel;
        
        try
        {
            _logger = Global.Services?.GetService<ILogger<SelectionTranslateWindowView>>();
        }
        catch { /* Ignore if services not available */ }
        
        // Close on Escape key
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    /// <summary>
    /// Sets the source text to be translated
    /// </summary>
    public void SetSourceText(string text)
    {
        _viewModel.SourceText = text;
    }
    
    /// <summary>
    /// Initializes the window with source text and waits for data processing to complete.
    /// </summary>
    public Task InitializeAsync(string text)
    {
        return _viewModel.InitializeAsync(text);
    }
    
    /// <summary>
    /// Gets the current source text
    /// </summary>
    public string GetSourceText()
    {
        return _viewModel.SourceText;
    }
    
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
    
    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
    
    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        var textToCopy = _viewModel.IsWordMode 
            ? _viewModel.DictionaryResult?.Word 
            : _viewModel.TranslationResult;
            
        if (string.IsNullOrWhiteSpace(textToCopy))
        {
            return;
        }
        
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(textToCopy);
                _logger?.LogDebug("Copied result to clipboard");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to copy to clipboard");
        }
    }
}
