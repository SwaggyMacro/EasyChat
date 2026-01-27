using EasyChat.Constants;
using EasyChat.Services.Languages;
using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

[JsonObject(MemberSerialization.OptIn)]
public class General : ReactiveObject
{
    [JsonProperty]
    public LanguageDefinition SourceLanguage
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = LanguageService.GetLanguage("auto");

    [JsonProperty]
    public LanguageDefinition TargetLanguage
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = LanguageService.GetLanguage("zh-Hans");


    [JsonProperty]
    public string? Language
    {
        get => field ?? "English";
        set => this.RaiseAndSetIfChanged(ref field, value ?? "English");
    } = "English";

    [JsonProperty]
    public WindowClosingBehavior ClosingBehavior
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = WindowClosingBehavior.Ask;

    [JsonProperty]
    public string? TransEngine
    {
        get => field ?? "AiModel";
        set
        {
            var newValue = value ?? "AiModel";
            this.RaiseAndSetIfChanged(ref field, newValue);
        }
    } = Constant.TransEngineType.Ai;

    [JsonProperty]
    public string? UsingAiModel
    {
        get => field ?? "OpenAI";
        set => this.RaiseAndSetIfChanged(ref field, value ?? "OpenAI");
    } = "OpenAI";

    [JsonProperty]
    public string? UsingAiModelId
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    [JsonProperty]
    public string? UsingMachineTransId
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    [JsonProperty]
    public string? UsingMachineTrans
    {
        get => field ?? "Baidu";
        set => this.RaiseAndSetIfChanged(ref field, value ?? "Baidu");
    } = "Baidu";
}