using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EasyChat.Services.Speech;
using EasyChat.Services.Speech.Tts;

namespace EasyChat.Services.Abstractions;

public interface ITtsService
{
    string ProviderId { get; }
    
    /// <summary>
    /// Gets the list of languages supported by this TTS provider.
    /// This may be a subset of the global TtsLanguageService.Languages.
    /// </summary>
    List<TtsLanguageDefinition> GetSupportedLanguages();

    /// <summary>
    /// Gets the list of voices available for this TTS provider.
    /// </summary>
    List<TtsVoiceDefinition> GetVoices();

    Task SynthesizeAsync(string text, string voiceId, string outputFile, string? rate = null, string? volume = null, string? pitch = null);
    
    Task<Stream?> StreamAsync(string text, string voiceId, string? rate = null, string? volume = null, string? pitch = null);
}
