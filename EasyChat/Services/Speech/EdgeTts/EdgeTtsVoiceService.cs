using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace EasyChat.Services.Speech.EdgeTts;

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

public interface IEdgeTtsVoiceProvider
{
    List<EdgeTtsVoiceDefinition> Voices { get; }
    Task InitializeAsync();
}

public class EdgeTtsVoiceProvider : IEdgeTtsVoiceProvider
{
    public List<EdgeTtsVoiceDefinition> Voices { get; private set; } = new();

    public async Task InitializeAsync()
    {
        if (Voices.Any()) return;

        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var appDirectory = Path.GetDirectoryName(assemblyLocation);
        var jsonPath = Path.Combine(appDirectory!, "Assets", "voices.json");

        var jsonString = await File.ReadAllTextAsync(jsonPath);
        var voices = JsonSerializer.Deserialize<List<EdgeTtsVoiceDefinition>>(jsonString);
        if (voices != null)
        {
            Voices = voices;
        }
    }
}
