using System.Collections.Generic;
using EasyChat.Services.Speech.Tts;
using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

[JsonObject(MemberSerialization.OptIn)]
public class TtsConfig : ReactiveObject
{
    [JsonProperty]
    public string Provider
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = TtsProviders.EdgeTTS;

    [JsonProperty]
    public Dictionary<string, Dictionary<string, string>> ProviderVoicePreferences
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = new();

    public string? GetVoiceForLanguage(string provider, string languageId)
    {
        if (ProviderVoicePreferences.TryGetValue(provider, out var voicePrefs))
        {
            if (voicePrefs.TryGetValue(languageId, out var voiceId))
            {
                return voiceId;
            }
        }
        return null;
    }

    public void SetVoiceForLanguage(string provider, string languageId, string voiceId)
    {
        if (!ProviderVoicePreferences.ContainsKey(provider))
        {
            ProviderVoicePreferences[provider] = new Dictionary<string, string>();
        }
        
        ProviderVoicePreferences[provider][languageId] = voiceId;
        this.RaisePropertyChanged(nameof(ProviderVoicePreferences));
    }

    public void RemoveVoiceForLanguage(string provider, string languageId)
    {
        if (ProviderVoicePreferences.TryGetValue(provider, out var voicePrefs))
        {
            if (voicePrefs.Remove(languageId))
            {
                 this.RaisePropertyChanged(nameof(ProviderVoicePreferences));
            }
        }
    }
}
