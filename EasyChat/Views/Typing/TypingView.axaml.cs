using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using EasyChat.Constants;
using EasyChat.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Key = Avalonia.Input.Key;

namespace EasyChat.Views.Typing;

public partial class TypingView : Window
{
    private readonly IPlatformService _platformService;
    private readonly ILogger<TypingView> _logger;
    private readonly IntPtr _targetHwnd;
    
    public TypingView(IntPtr targetHwnd)
    {
        InitializeComponent();
        
        ApplyConfiguration();
        
        // Listen for config changes
        var configService = Global.Services?.GetRequiredService<IConfigurationService>();
        if (configService?.Input != null)
        {
            configService.Input.Changed.Subscribe(_ => 
                Dispatcher.UIThread.Post(ApplyConfiguration));
        }
        
        _targetHwnd = targetHwnd;
        _platformService = Global.Services?.GetRequiredService<IPlatformService>() ??
                           throw new InvalidOperationException("PlatformService not found");
        _logger = Global.Services?.GetRequiredService<ILogger<TypingView>>() ??
                  throw new InvalidOperationException("Logger not found");

        var inputBox = this.FindControl<TextBox>("InputBox");

        Opened += (s, e) => { inputBox?.Focus(); };

        LostFocus += (s, e) => { Close(); };
    }

    private void ApplyConfiguration()
    {
        var configService = Global.Services?.GetRequiredService<IConfigurationService>();
        var config = configService?.Input;
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

            // Hide immediately or waiting? 
            // Better to hide or show loading state.
            inputBox.IsEnabled = false;

            try
            {

                var translatedText = await TranslateText(text);

                // Hide first
                Hide();

                var delay = 10;
                var mode = Models.Configuration.InputDeliveryMode.Type;

                if (Global.Services?.GetRequiredService<IConfigurationService>()?.Input is { } inputConfig)
                {
                    delay = inputConfig.KeySendDelay;
                    mode = inputConfig.DeliveryMode;
                }

                // Ensure focus with retry
                if (await _platformService.EnsureFocused(_targetHwnd))
                {
                     // Wait a bit for focus to settle completely
                     await Task.Delay(100);

                     // Send text
                     if (mode == Models.Configuration.InputDeliveryMode.Paste)
                     {
                         await _platformService.PasteTextAsync(translatedText);
                     }
                     else if (mode == Models.Configuration.InputDeliveryMode.Message)
                     {
                         await _platformService.SendTextMessageAsync(_targetHwnd, translatedText, delay);
                     }
                     else
                     {
                         await _platformService.SendTextAsync(translatedText, delay);
                     }
                }
                else
                {
                    _logger.LogWarning("Failed to ensure focus on target window, skipping text delivery.");
                }
                
                // Finally close
                Close();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Translation failed");
                Close();
            }
        }
        else if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private async Task<string> TranslateText(string text)
    {
        try
        {
            var services = Global.Services;
            if (services == null) return $"[Error] Services not initialized.";

            var factory = services.GetRequiredService<ITranslationServiceFactory>();
            var configService = services.GetRequiredService<IConfigurationService>();
            
            var translator = factory.CreateCurrentService();
            var sourceLang = configService.General.SourceLanguage;
            var targetLang = configService.General.TargetLanguage;

            if (translator is Services.Translation.Ai.OpenAiService openAi)
            {
                var result = "";
                await foreach (var chunk in openAi.StreamTranslateAsync(text, targetLang, sourceLang))
                {
                    result += chunk;
                }
                return result;
            }
            
            return await translator.TranslateAsync(text, targetLang, sourceLang);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during translation");
            return $"[Error] {ex.Message}";
        }
    }
}