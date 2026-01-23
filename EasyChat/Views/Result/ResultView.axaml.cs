using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using EasyChat.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyChat.Views.Result;

public partial class ResultView : Window
{
    private readonly Screen _screen;

    public ResultView()
    {
        InitializeComponent();
        
        ApplyConfiguration();
        
        // Default state
        ShowLoading();
        IsVisible = false;

        _screen = GetScreen()!;
        TextBlockResult.MaxWidth = _screen.Bounds.Width / _screen.Scaling * 0.8;
        ReCenterPosition();
        Loaded += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                ReCenterPosition();
                // Ensure the window is not closed before showing it
                if (IsLoaded)
                {
                    IsVisible = true;
                }
            });
        };
    }

    private void ApplyConfiguration()
    {
        var configService = Global.Services?.GetRequiredService<IConfigurationService>();
        var config = configService?.Result;
        if (config == null) return;

        // Transparency Level Hint
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
                 default:
                     TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
                     break;
             }
        }

        // Background Color (GlassCard)
        if (!string.IsNullOrEmpty(config.BackgroundColor))
        {
            try
            {
                var brush = Brush.Parse(config.BackgroundColor);
                this.FindControl<SukiUI.Controls.GlassCard>("MainCard")!.Background = brush;
            }
            catch { /* Ignore invalid color */ }
        }
        
        // Window Background Color
        if (!string.IsNullOrEmpty(config.WindowBackgroundColor))
        {
            try
            {
                var brush = Brush.Parse(config.WindowBackgroundColor);
                Background = brush;
            }
            catch { /* Ignore invalid color */ }
        }
        
        // Font Color
        if (!string.IsNullOrEmpty(config.FontColor))
        {
            try
            {
                var brush = Brush.Parse(config.FontColor);
                TextBlockResult.Foreground = brush;
            }
            catch { /* Ignore invalid color */ }
        }
        
        // Font Size
        TextBlockResult.FontSize = config.FontSize;
    }

    public ResultView(string result)
    {
        InitializeComponent();
        TextBlockResult.Text = result;
        ShowResult(); // Directly show result if text is provided
        IsVisible = false;

        _screen = GetScreen()!;
        TextBlockResult.MaxWidth = _screen.Bounds.Width / _screen.Scaling * 0.8;
        ReCenterPosition();
        Loaded += async (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                ReCenterPosition();
                if (IsLoaded)
                {
                    IsVisible = true;
                }
            });
            await Task.Delay(5000);
            Close();
        };
    }

    private Screen? GetScreen()
    {
        foreach (var screen in Global.Screens.All)
            if (screen.Bounds.Contains(new PixelPoint(Position.X, Position.Y)))
                return screen;

        return Global.Screens.Primary;
    }

    public void AppendText(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (LoadingIndicator.IsVisible)
            {
                ShowResult();
            }
            TextBlockResult.Text += text;
            Dispatcher.UIThread.Post(ReCenterPosition);
        });
    }

    public void ShowLoading()
    {
        Dispatcher.UIThread.Post(() =>
        {
            LoadingIndicator.IsVisible = true;
            TextBlockResult.IsVisible = false;
        });
    }

    public void ShowResult()
    {
        Dispatcher.UIThread.Post(() =>
        {
            LoadingIndicator.IsVisible = false;
            TextBlockResult.IsVisible = true;
            ReCenterPosition();
        });
    }

    public void CloseAfterDelay(int milliseconds)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            await Task.Delay(milliseconds);
            Close();
        });
    }

    public void SetFontSize(double size)
    {
        Dispatcher.UIThread.Post(() =>
        {
            TextBlockResult.FontSize = size;
        });
    }

    [SuppressMessage("ReSharper", "PossibleLossOfFraction")]
    private void ReCenterPosition()
    {
        // TODO: Screen Scale needs to be solved
        var width = Width;
        var x = _screen.Bounds.Width / _screen.Scaling / 2 - width / 2;
        Position = new PixelPoint((int)x, -5);
    }
}