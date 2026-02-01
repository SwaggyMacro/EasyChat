using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

[JsonObject(MemberSerialization.OptIn)]
public class SelectionTranslationConfig : ReactiveObject
{
    private bool _enabled;

    private string _provider = "AI";
    private string? _aiModelId;

    [JsonProperty]
    public bool Enabled
    {
        get => _enabled;
        set => this.RaiseAndSetIfChanged(ref _enabled, value);
    }

    [JsonProperty]
    public string Provider
    {
        get => _provider;
        set => this.RaiseAndSetIfChanged(ref _provider, value);
    }

    [JsonProperty]
    public string? AiModelId
    {
        get => _aiModelId;
        set => this.RaiseAndSetIfChanged(ref _aiModelId, value);
    }
}
