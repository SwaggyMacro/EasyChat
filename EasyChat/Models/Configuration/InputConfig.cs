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

    private string _fontColor = "#FFFFFFFF"; // Default white
    [JsonProperty]
    public string FontColor
    {
        get => _fontColor;
        set => this.RaiseAndSetIfChanged(ref _fontColor, value);
    }

    private int _keySendDelay = 10;
    [JsonProperty]
    public int KeySendDelay
    {
        get => _keySendDelay;
        set => this.RaiseAndSetIfChanged(ref _keySendDelay, value);
    }

    private InputDeliveryMode _deliveryMode = InputDeliveryMode.Type;
    [JsonProperty]
    public InputDeliveryMode DeliveryMode
    {
        get => _deliveryMode;
        set => this.RaiseAndSetIfChanged(ref _deliveryMode, value);
    }
}

public enum InputDeliveryMode
{
    Type,
    Paste,
    Message
}
