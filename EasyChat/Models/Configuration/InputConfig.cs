using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

[JsonObject(MemberSerialization.OptIn)]
public class InputConfig : ReactiveObject
{
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
    } = "#CC000000";

    [JsonProperty]
    public string FontColor
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "#FFFFFFFF";

    [JsonProperty]
    public int KeySendDelay
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 10;

    [JsonProperty]
    public InputDeliveryMode DeliveryMode
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = InputDeliveryMode.Paste;

    [JsonProperty]
    public bool ReverseTranslateLanguage
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    [JsonProperty]
    public string TypingSourceLanguage
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "auto";

    [JsonProperty]
    public string TypingTargetLanguage
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "en";

    [JsonProperty]
    public bool FollowGlobalLanguage
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;
}

public enum InputDeliveryMode
{
    Type,
    Paste,
    Message
}

