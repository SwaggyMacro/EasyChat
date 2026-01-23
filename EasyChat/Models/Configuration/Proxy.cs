using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

[JsonObject(MemberSerialization.OptIn)]
public class Proxy : ReactiveObject
{
    private string _proxyUrl = string.Empty;

    [JsonProperty]
    public string ProxyUrl
    {
        get => _proxyUrl;
        set => this.RaiseAndSetIfChanged(ref _proxyUrl, value);
    }
}