using System.Collections.Generic;

namespace EasyChat.Services.Speech.Tts;

public class TtsVoiceDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string LanguageId { get; init; }
    public required string Gender { get; init; }
    public List<string> ContentCategories { get; init; } = new();
    public List<string> VoicePersonalities { get; init; } = new();
    
    // Original provider specific mapping can be stored if really needed, but usually Id is enough.
}
