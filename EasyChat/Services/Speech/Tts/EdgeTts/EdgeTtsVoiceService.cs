using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace EasyChat.Services.Speech.Tts.EdgeTts;

public class EdgeTtsVoiceProvider
{
    public List<EdgeTtsVoiceDefinition> Voices { get; private set; } = new();

    public List<TtsVoiceDefinition> GetGenericVoices()
    {
        return Voices.Select(v => new TtsVoiceDefinition
        {
            Id = v.Name,
            Name = v.Role,
            LanguageId = GetLanguageIdFromName(v.Name),
            Gender = v.Gender,
            ContentCategories = ParseList(v.ContentCategories),
            VoicePersonalities = ParseList(v.VoicePersonalities)
        }).ToList();
    }

    private string GetLanguageIdFromName(string name)
    {
        var parts = name.Split('-');
        if (parts.Length >= 3)
        {
             if (name.StartsWith("iu-Cans-CA") || name.StartsWith("iu-Latn-CA")) return string.Join("-", parts[0], parts[1], parts[2]);
             if (name.StartsWith("zh-CN-liaoning") || name.StartsWith("zh-CN-shaanxi")) return string.Join("-", parts[0], parts[1], parts[2]);
             
             return $"{parts[0]}-{parts[1]}";
        }
        return "unknown";
    }

    private List<string> ParseList(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new List<string>();
        return input.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries).ToList();
    }

    public async Task InitializeAsync()
    {
        if (Voices.Any()) return;

        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var appDirectory = Path.GetDirectoryName(assemblyLocation);
        var jsonPath = Path.Combine(appDirectory!, "Assets", "voices.json");

        if (File.Exists(jsonPath))
        {
            var jsonString = await File.ReadAllTextAsync(jsonPath);
            var voices = JsonSerializer.Deserialize<List<EdgeTtsVoiceDefinition>>(jsonString);
            if (voices != null)
            {
                Voices = voices;
            }
        }
    }
}
