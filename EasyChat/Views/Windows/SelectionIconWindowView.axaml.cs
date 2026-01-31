using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Material.Icons.Avalonia;
using SukiUI.Controls;

namespace EasyChat.Views.Windows;

public partial class SelectionIconWindowView : Window
{
    public event EventHandler? TranslateClicked;
    
    private MaterialIcon? _translateIcon;
    private Loading? _loadingSpinner;
    private bool _isLoading;

    public SelectionIconWindowView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        
        // Find named controls
        _translateIcon = this.FindControl<MaterialIcon>("TranslateIcon");
        _loadingSpinner = this.FindControl<Loading>("LoadingSpinner");
    }
    
    /// <summary>
    /// Shows the loading spinner and hides the translate icon
    /// </summary>
    public void ShowLoading()
    {
        _isLoading = true;
        if (_translateIcon != null) _translateIcon.IsVisible = false;
        if (_loadingSpinner != null) _loadingSpinner.IsVisible = true;
    }
    
    /// <summary>
    /// Hides the loading spinner and shows the translate icon
    /// </summary>
    public void HideLoading()
    {
        _isLoading = false;
        if (_translateIcon != null) _translateIcon.IsVisible = true;
        if (_loadingSpinner != null) _loadingSpinner.IsVisible = false;
    }
    
    /// <summary>
    /// Gets whether the icon is currently in loading state
    /// </summary>
    public bool IsLoading => _isLoading;
    
    private void OnIconPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("SelectionIconWindow: Icon pressed!");
        
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
            TranslateClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
