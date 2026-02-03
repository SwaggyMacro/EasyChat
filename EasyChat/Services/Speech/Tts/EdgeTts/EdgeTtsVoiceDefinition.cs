using System.Diagnostics.CodeAnalysis;

namespace EasyChat.Services.Speech.Tts.EdgeTts;

[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public class EdgeTtsVoiceDefinition
{
    public required string Name { get; set; }
    public required string Gender { get; set; }
    public required string ContentCategories { get; set; }
    public required string VoicePersonalities { get; set; }
    public required string EnglishName { get; set; }
    public required string ChineseName { get; set; }
    public required string Role { get; set; }
    public required string Language { get; set; }
    public required string Region { get; set; }

    public override string ToString()
    {
        return EnglishName; 
    }
}
