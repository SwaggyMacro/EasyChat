using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EasyChat.Services.Abstractions;

namespace EasyChat.Services.Speech.Tts;

public class TtsManager : ITtsService
{
    private readonly IEnumerable<ITtsService> _providers;
    private ITtsService _currentProvider = default!; // Initialized in constructor via SwitchToDefault

    public string ProviderId => _currentProvider.ProviderId;

    public TtsManager(IEnumerable<ITtsService> providers)
    {
        _providers = providers;
        SwitchToDefault();
    }

    private void SwitchToDefault()
    {
        // Default to EdgeTTS if available, otherwise first
        _currentProvider = _providers.FirstOrDefault(p => p.ProviderId == TtsProviders.EdgeTTS) 
                           ?? _providers.FirstOrDefault() 
                           ?? throw new InvalidOperationException("No TTS providers registered.");
    }

    public void SwitchProvider(string providerId)
    {
        var provider = _providers.FirstOrDefault(p => p.ProviderId == providerId);
        if (provider != null)
        {
            _currentProvider = provider;
        }
    }

    public List<string> GetAvailableProviders()
    {
        return _providers.Select(p => p.ProviderId).ToList();
    }

    public List<TtsLanguageDefinition> GetSupportedLanguages()
    {
        return _currentProvider.GetSupportedLanguages();
    }

    public List<TtsVoiceDefinition> GetVoices()
    {
        return _currentProvider.GetVoices();
    }

    public Task SynthesizeAsync(string text, string voiceId, string outputFile, string? rate = null, string? volume = null, string? pitch = null)
    {
        return _currentProvider.SynthesizeAsync(text, voiceId, outputFile, rate, volume, pitch);
    }

    public Task<Stream> StreamAsync(string text, string voiceId, string? rate = null, string? volume = null, string? pitch = null)
    {
        return _currentProvider.StreamAsync(text, voiceId, rate, volume, pitch);
    }
}
