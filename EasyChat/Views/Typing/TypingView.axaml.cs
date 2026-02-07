using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using EasyChat.Common;
using EasyChat.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Key = Avalonia.Input.Key;

using EasyChat.ViewModels.Typing;

namespace EasyChat.Views.Typing;

public partial class TypingView : Window
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public TypingView() {}
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public TypingView(IntPtr targetHwnd)
    {
        InitializeComponent();
        
        DataContext = new TypingViewModel(targetHwnd);
        
        ApplyConfiguration();
        
        // Listen for config changes
        var configService = Global.Services?.GetRequiredService<IConfigurationService>();
        configService?.Input?.Changed.Subscribe(_ => 
            Dispatcher.UIThread.Post(ApplyConfiguration));
        
        var inputBox = this.FindControl<TextBox>("InputBox");

        Opened += (_, _) => { inputBox?.Focus(); };
        
        Deactivated += (_, _) =>
        {
            // If any ComboBox dropdown is open, do not close.
            var comboBoxes = this.GetVisualDescendants().OfType<ComboBox>();
            if (comboBoxes.Any(cb => cb.IsDropDownOpen))
            {
                return;
            }
            
            Close();
        };
    }

    private void ApplyConfiguration()
    {
        var configService = Global.Services?.GetRequiredService<IConfigurationService>();
        var config = configService?.Input;
        if (config == null) return;

        // Transparency Level Hint
        if (!string.IsNullOrEmpty(config.TransparencyLevel))
        {
             switch (config.TransparencyLevel)
             {
                 case "AcrylicBlur":
                     TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur];
                     break;
                 case "Blur":
                     TransparencyLevelHint = [WindowTransparencyLevel.Blur];
                     break;
                 case "Transparent":
                     TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
                     break;
                 // Add others if needed like Mica
                 default:
                     TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
                     break;
             }
        }

        // Background Color
        if (!string.IsNullOrEmpty(config.BackgroundColor))
        {
            try
            {
                var brush = Avalonia.Media.Brush.Parse(config.BackgroundColor);
                this.FindControl<SukiUI.Controls.GlassCard>("MainCard")!.Background = brush;
            }
            catch { /* Ignore invalid color */ }
        }

        // Font Color
        if (!string.IsNullOrEmpty(config.FontColor))
        {
            try
            {
                 var brush = Avalonia.Media.Brush.Parse(config.FontColor);
                 var inputBox = this.FindControl<TextBox>("InputBox");
                 if (inputBox != null) inputBox.Foreground = brush;
            }
            catch { /* Ignore invalid color */ }
        }
    }

    private async void InputBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        var inputBox = sender as TextBox;
        if (inputBox == null) return;

        if (e.Key == Key.Enter)
        {
            var text = inputBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                Close();
                return;
            }

            inputBox.IsEnabled = false;

            if (DataContext is TypingViewModel vm)
            {
                // Hide first
                Hide();
                
                await vm.TranslateAndSendAsync(text);
                
                Close();
            }
            else
            {
                Close();
            }
        }
        else if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}