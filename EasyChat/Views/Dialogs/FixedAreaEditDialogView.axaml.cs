using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using EasyChat.Models.Configuration;
using EasyChat.ViewModels.Dialogs;

namespace EasyChat.Views.Dialogs;

public partial class FixedAreaEditDialogView : UserControl
{
    public FixedAreaEditDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    private void EditButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is FixedArea area && DataContext is FixedAreaEditDialogViewModel vm)
        {
            vm.EditAreaCommand.Execute(area).Subscribe(_ => { });
        }
    }
    
    private void DeleteButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is FixedArea area && DataContext is FixedAreaEditDialogViewModel vm)
        {
            vm.DeleteAreaCommand.Execute(area).Subscribe(_ => { });
        }
    }
}
