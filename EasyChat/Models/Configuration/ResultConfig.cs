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
    
    private string _transparencyLevel = "Transparent";
    private string _backgroundColor = "#CC000000";
    private string _fontColor = "#FFFFFFFF";

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

    [JsonProperty]
    public string FontColor
    {
        get => _fontColor;
        set => this.RaiseAndSetIfChanged(ref _fontColor, value);
    }

    private string _windowBackgroundColor = "#00000000";

    [JsonProperty]
    public string WindowBackgroundColor
    {
        get => _windowBackgroundColor;
        set => this.RaiseAndSetIfChanged(ref _windowBackgroundColor, value);
    }
}
