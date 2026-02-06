using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

[JsonObject(MemberSerialization.OptIn)]
public class ResultConfig : ReactiveObject
{
    private int _autoCloseDelay = 5000;
    [JsonProperty]
    public int AutoCloseDelay
    {
        get => _autoCloseDelay;
        set => this.RaiseAndSetIfChanged(ref _autoCloseDelay, value);
    }

    private double _fontSize = 18;
    [JsonProperty]
    public double FontSize
    {
        get => _fontSize;
        set => this.RaiseAndSetIfChanged(ref _fontSize, value);
    }

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

    private string _transparencyLevel = "AcrylicBlur";
    [JsonProperty]
    public string TransparencyLevel
    {
        get => _transparencyLevel;
        set => this.RaiseAndSetIfChanged(ref _transparencyLevel, value);
    }

    private string _backgroundColor = "#00000000";
    [JsonProperty]
    public string BackgroundColor
    {
        get => _backgroundColor;
        set => this.RaiseAndSetIfChanged(ref _backgroundColor, value);
    }

    private string _fontColor = "#FFFFFFFF";
    [JsonProperty]
    public string FontColor
    {
        get => _fontColor;
        set => this.RaiseAndSetIfChanged(ref _fontColor, value);
    }

    private string _fontFamily = "";
    [JsonProperty]
    public string FontFamily
    {
        get => _fontFamily;
        set => this.RaiseAndSetIfChanged(ref _fontFamily, value);
    }

    private string _windowBackgroundColor = "#CC000000";
    [JsonProperty]
    public string WindowBackgroundColor
    {
        get => _windowBackgroundColor;
        set => this.RaiseAndSetIfChanged(ref _windowBackgroundColor, value);
    }

    private ResultWindowMode _screenshotResultMode = ResultWindowMode.Classic;
    [JsonProperty]
    public ResultWindowMode ScreenshotResultMode
    {
        get => _screenshotResultMode;
        set => this.RaiseAndSetIfChanged(ref _screenshotResultMode, value);
    }
}

public enum ResultWindowMode
{
    Classic,
    Dictionary
}
