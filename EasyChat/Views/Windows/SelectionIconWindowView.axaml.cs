using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using EasyChat.Common;
using EasyChat.Services.Abstractions;
using Material.Icons.Avalonia;
using Microsoft.Extensions.DependencyInjection;
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
        
        // Apply no-activate style when window is opened (prevents focus stealing)
        Opened += (_, _) => ApplyNoActivateStyle();
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
