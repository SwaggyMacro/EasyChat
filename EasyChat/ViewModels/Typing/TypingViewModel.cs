using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using EasyChat.Common;
using EasyChat.Models.Configuration;
using EasyChat.Services.Abstractions;
using EasyChat.Services.Languages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace EasyChat.ViewModels.Typing;

public class TypingViewModel : ReactiveObject
{

    private readonly IConfigurationService? _configService;
    private readonly InputConfig? _inputConfig;

    public IEnumerable<LanguageDefinition> SourceLanguages => LanguageService.GetAllLanguages();
    public IEnumerable<LanguageDefinition> TargetLanguages => LanguageService.GetAllLanguages();

    public LanguageDefinition? SelectedSourceLanguage
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            _inputConfig?.TypingSourceLanguage = value?.Id ?? LanguageKeys.Auto.Id;
        }
    }

    public LanguageDefinition? SelectedTargetLanguage
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            if (_inputConfig != null)
            {
                _inputConfig.TypingTargetLanguage = value?.Id ?? LanguageKeys.English.Id;
            }
        }
    }

    private readonly IPlatformService _platformService;
    private readonly ITranslationServiceFactory _translationServiceFactory;
    private readonly ILogger<TypingViewModel> _logger;
    private readonly IntPtr _targetHwnd;

    private bool _followGlobalLanguage;
    public bool FollowGlobalLanguage
    {
        get => _followGlobalLanguage;
        set
        {
             this.RaiseAndSetIfChanged(ref _followGlobalLanguage, value);
             if (_inputConfig != null && _inputConfig.FollowGlobalLanguage != value)
             {
                 _inputConfig.FollowGlobalLanguage = value;
             }
        }
    }

    public TypingViewModel(IntPtr targetHwnd)
    {
        _targetHwnd = targetHwnd;
        _configService = Global.Services?.GetRequiredService<IConfigurationService>()!;
        _inputConfig = _configService?.Input!;
        _platformService = Global.Services?.GetRequiredService<IPlatformService>()!;
        _translationServiceFactory = Global.Services?.GetRequiredService<ITranslationServiceFactory>()!;
        _logger = Global.Services?.GetRequiredService<ILogger<TypingViewModel>>()!;

        // Initialize Languages
        // SourceLanguages/TargetLanguages are properties now using LanguageService directly.
        
        // Set initial selection
        UpdateFromConfig();
        
        // Listen for config changes
        _inputConfig?.Changed.Subscribe(_ => 
        {
            // Update UI on UI thread if needed, but ReactiveUI properties should handle it.
            // However, we need to sync *from* config if it changes externally.
            UpdateFromConfig();
        });
    }

    private void UpdateFromConfig()
    {
        if (_inputConfig == null) return;
        
        if (FollowGlobalLanguage != _inputConfig.FollowGlobalLanguage)
        {
            FollowGlobalLanguage = _inputConfig.FollowGlobalLanguage;
        }

        var sourceId = _inputConfig.TypingSourceLanguage;
        var targetId = _inputConfig.TypingTargetLanguage;

        if (SelectedSourceLanguage?.Id != sourceId)
        {
             SelectedSourceLanguage = SourceLanguages.FirstOrDefault(x => x.Id == sourceId) ?? SourceLanguages.FirstOrDefault();
        }
        
        if (SelectedTargetLanguage?.Id != targetId)
        {
             SelectedTargetLanguage = TargetLanguages.FirstOrDefault(x => x.Id == targetId) ?? TargetLanguages.FirstOrDefault();
        }
    }

    public async Task TranslateAndSendAsync(string text)
    {
        try
        {
            var translatedText = await TranslateText(text);

            var delay = 10;
            var mode = InputDeliveryMode.Type;

            if (_inputConfig != null)
            {
                delay = _inputConfig.KeySendDelay;
                mode = _inputConfig.DeliveryMode;
            }

            // Ensure focus with retry
            if (await _platformService.EnsureFocused(_targetHwnd))
            {
                 // Wait a bit for focus to settle completely
                 await Task.Delay(100);

                 // Send text
                 if (mode == InputDeliveryMode.Paste)
                 {
                     // Backup clipboard logic using helper
                     var backup = await ClipboardHelper.BackupClipboardAsync(_logger);

                     await _platformService.PasteTextAsync(translatedText);
                     
                     // Wait for paste to complete before restoring
                     await Task.Delay(200);
                     
                     // Restore clipboard
                     await ClipboardHelper.RestoreClipboardAsync(backup, _logger);
                 }
                 else if (mode == InputDeliveryMode.Message)
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation/Delivery failed");
        }
    }

    private async Task<string> TranslateText(string text)
    {
        try
        {
            var translator = _translationServiceFactory.CreateCurrentService();
            var sourceLang = _configService?.General?.SourceLanguage;
            var targetLang = _configService?.General?.TargetLanguage;
            
            // Override with Typing View Settings
            if (_inputConfig is { FollowGlobalLanguage: false })
            {
                if (_inputConfig.TypingSourceLanguage is { } typingSource)
                {
                    sourceLang = LanguageService.GetLanguage(typingSource);
                }
                if (_inputConfig.TypingTargetLanguage is { } typingTarget)
                {
                    targetLang = LanguageService.GetLanguage(typingTarget);
                }
            }

            if (_inputConfig?.ReverseTranslateLanguage == true)
            {
                (sourceLang, targetLang) = (targetLang ?? throw new InvalidOperationException("Target language not configured"), 
                    sourceLang ?? throw new InvalidOperationException("Source language not configured"));
            }

            if (translator is Services.Translation.Ai.OpenAiService openAi)
            {
                var result = "";
                await foreach (var chunk in openAi.StreamTranslateAsync(text, sourceLang, targetLang))
                {
                    result += chunk;
                }
                return result;
            }
            
            return await translator.TranslateAsync(text, sourceLang, targetLang);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during translation");
            return $"[Error] {ex.Message}";
        }
    }
}
