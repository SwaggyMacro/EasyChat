using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

[JsonObject(MemberSerialization.OptIn)]
public class InputConfig : ReactiveObject
{
    private string _transparencyLevel = "Transparent";
    private string _backgroundColor = "#CC000000";

    [JsonProperty]
    public string TransparencyLevel
    {
        get => _transparencyLevel;
        set => this.RaiseAndSetIfChanged(ref _transparencyLevel, value);
    }

    [JsonProperty]
    public string BackgroundColor
    {
        get => _backgroundColor;
        set => this.RaiseAndSetIfChanged(ref _backgroundColor, value);
    }
}
