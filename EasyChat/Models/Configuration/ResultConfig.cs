using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

[JsonObject(MemberSerialization.OptIn)]
public class ResultConfig : ReactiveObject
{
    [JsonProperty]
    public int AutoCloseDelay
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 5000;

    [JsonProperty]
    public double FontSize
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 18;

    private bool _enableAutoReadDelay;
    private int _msPerChar = 50;

    [JsonProperty]
    public bool EnableAutoReadDelay
    {
        get => _enableAutoReadDelay;
        set => this.RaiseAndSetIfChanged(ref _enableAutoReadDelay, value);
    }

    [JsonProperty]
    public int MsPerChar
    {
        get => _msPerChar;
        set => this.RaiseAndSetIfChanged(ref _msPerChar, value);
    }

    [JsonProperty]
    public string TransparencyLevel
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "AcrylicBlur";

    [JsonProperty]
    public string BackgroundColor
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "#00000000";

    [JsonProperty]
    public string FontColor
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "#FFFFFFFF";

    [JsonProperty]
    public string FontFamily
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "";

    [JsonProperty]
    public string WindowBackgroundColor
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "#CC000000";
}
