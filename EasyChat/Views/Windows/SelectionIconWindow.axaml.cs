using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace EasyChat.Views.Windows;

public partial class SelectionIconWindow : Window
{
    public event EventHandler? TranslateClicked;

    public SelectionIconWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
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
