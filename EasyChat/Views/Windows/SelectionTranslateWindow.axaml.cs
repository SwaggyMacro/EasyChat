using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using EasyChat.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SukiUI.Controls;

namespace EasyChat.Views.Windows;

public partial class SelectionTranslateWindow : Window
{
    private readonly SelectableTextBlock _sourceTextBlock;
    private readonly SelectableTextBlock _resultTextBlock;
    private readonly Loading _loadingIndicator;
    private readonly ILogger<SelectionTranslateWindow>? _logger;
    
    public SelectionTranslateWindow()
    {
        InitializeComponent();
        
        _sourceTextBlock = this.FindControl<SelectableTextBlock>("SourceTextBlock")!;
        _resultTextBlock = this.FindControl<SelectableTextBlock>("ResultTextBlock")!;
        _loadingIndicator = this.FindControl<Loading>("LoadingIndicator")!;
        
        try
        {
            _logger = Global.Services?.GetService<ILogger<SelectionTranslateWindow>>();
        }
        catch { /* Ignore if services not available */ }
        
        // Removed automatic focus to prevent stealing focus
        
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
        _sourceTextBlock.Text = text;
    }
    
    /// <summary>
    /// Gets the current source text
    /// </summary>
    public string GetSourceText()
    {
        return _sourceTextBlock.Text ?? string.Empty;
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
    
    private async void OnTranslateClick(object? sender, RoutedEventArgs e)
    {
        var sourceText = GetSourceText();
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return;
        }
        
        try
        {
            _loadingIndicator.IsVisible = true;
            _resultTextBlock.Text = "";
            
            // TODO: Implement actual translation using ITranslationServiceFactory
            // For now, just show placeholder
            await Task.Delay(500); // Simulate translation
            
            _resultTextBlock.Text = Lang.Resources.SelectionTranslate_ImplementationPending + sourceText;
            
            _logger?.LogInformation("Translation requested for text: {Length} chars", sourceText.Length);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Translation failed");
            _resultTextBlock.Text = Lang.Resources.SelectionTranslate_Failed + ex.Message;
        }
        finally
        {
            _loadingIndicator.IsVisible = false;
        }
    }
    
    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        var resultText = _resultTextBlock.Text;
        if (string.IsNullOrWhiteSpace(resultText))
        {
            return;
        }
        
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(resultText);
                _logger?.LogDebug("Copied result to clipboard");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to copy to clipboard");
        }
    }
}
