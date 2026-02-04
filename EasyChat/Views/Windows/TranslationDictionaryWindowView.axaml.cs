using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using EasyChat.Common;
using EasyChat.Services.Abstractions;
using EasyChat.ViewModels.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyChat.Views.Windows;

public partial class TranslationDictionaryWindowView : Window
{
    private readonly TranslationDictionaryWindowViewModel _viewModel;
    private readonly ILogger<TranslationDictionaryWindowView>? _logger;
    
    public TranslationDictionaryWindowView()
    {
        InitializeComponent();
        
        _viewModel = Global.Services?.GetService<TranslationDictionaryWindowViewModel>() 
                     ?? throw new InvalidOperationException("Failed to resolve ViewModel");
        DataContext = _viewModel;
        
        try
        {
            _logger = Global.Services.GetService<ILogger<TranslationDictionaryWindowView>>();
        }
        catch { /* Ignore if services not available */ }
        
        // Apply no-activate style when window is opened (prevents focus stealing)
        Opened += (_, _) => ApplyNoActivateStyle();
        
        // Close on Escape key
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
            {
                Close();
            }
        };
    }

    private void ApplyNoActivateStyle()
    {
        var handle = TryGetPlatformHandle()?.Handle;
        if (handle != null && handle != IntPtr.Zero)
        {
            var focusService = Global.Services?.GetService<IFocusService>();
            focusService?.SetWindowNoActivate(handle.Value);
        }
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

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        string textToCopy;

        if (_viewModel.IsWordMode && _viewModel.DictionaryResult != null)
        {
            // Format:
            // Word [Phonetic]
            // Part: Def1; Def2...
            //
            // Examples:
            // Origin -> Translation
            
            var sb = new System.Text.StringBuilder();
            var dr = _viewModel.DictionaryResult;
            
            sb.Append(dr.Word);
            if (!string.IsNullOrWhiteSpace(dr.Phonetic))
            {
                sb.Append($" {dr.Phonetic}");
            }
            sb.AppendLine();
            
            if (dr.Parts != null)
            {
                foreach (var part in dr.Parts)
                {
                    sb.AppendLine($"{part.PartOfSpeech} {string.Join("; ", part.Definitions)}");
                }
            }

            if (dr.Examples != null && dr.Examples.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Examples:");
                foreach (var ex in dr.Examples)
                {
                    sb.AppendLine($"{ex.Origin} -> {ex.Translation}");
                }
            }
            
            textToCopy = sb.ToString().Trim();
        }
        else
        {
            textToCopy = _viewModel.TranslationResult;
        }
            
        if (string.IsNullOrWhiteSpace(textToCopy))
        {
            return;
        }
        
        try
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
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
    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnResizePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginResizeDrag(WindowEdge.SouthEast, e);
        }
    }
}
