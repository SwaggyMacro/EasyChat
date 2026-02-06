using System;
using System.Linq;
using EasyChat.Services.Abstractions;
using EasyChat.Services.Languages;

namespace EasyChat.Services.Speech.Tts;

public static class TtsHelper
{
    public static string? GetPreferredVoiceId(ITtsService ttsService, IConfigurationService configService, string langId)
    {
        // Get Voice from Config
        var provider = configService.Tts?.Provider ?? TtsProviders.EdgeTTS;
        var voiceId = configService.Tts?.GetVoiceForLanguage(provider, langId);
        
        if (string.IsNullOrEmpty(voiceId))
        {
             // Fallback: pick first available voice for language or default
             var voices = ttsService.GetVoices();
             var match = voices.FirstOrDefault(v => v.LanguageId.StartsWith(langId.Split("-").FirstOrDefault() ?? langId, StringComparison.OrdinalIgnoreCase)) ??
                         // If specific lang not found and we are defaulting to EN, try generic EN
                         voices.FirstOrDefault(v => v.Id.Contains(LanguageKeys.EnglishId));

             voiceId = match?.Id;
        }

        return voiceId;
    }
}
