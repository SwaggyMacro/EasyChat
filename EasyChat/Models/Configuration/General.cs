using EasyChat.Constants;
using EasyChat.Services.Languages;
using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

[JsonObject(MemberSerialization.OptIn)]
public class General : ReactiveObject
{
    private string _language = "English";
    private WindowClosingBehavior _closingBehavior = WindowClosingBehavior.Ask;

    private string _transEngine = Constant.TransEngineType.Ai;

    private string _usingAiModel = "OpenAI";
    private string? _usingAiModelId;

    private string _usingMachineTrans = "Baidu";

    private LanguageDefinition _sourceLanguage = LanguageService.GetLanguage("auto");
    private LanguageDefinition _targetLanguage = LanguageService.GetLanguage("zh-Hans");

    [JsonProperty]
    public LanguageDefinition SourceLanguage
    {
        get => _sourceLanguage;
        set => this.RaiseAndSetIfChanged(ref _sourceLanguage, value);
    }

    [JsonProperty]
    public LanguageDefinition TargetLanguage
    {
        get => _targetLanguage;
        set => this.RaiseAndSetIfChanged(ref _targetLanguage, value);
    }


    [JsonProperty]
    public string Language
    {
        get => _language ?? "English";
        set => this.RaiseAndSetIfChanged(ref _language, value ?? "English");
    }

    [JsonProperty]
    public WindowClosingBehavior ClosingBehavior
    {
        get => _closingBehavior;
        set => this.RaiseAndSetIfChanged(ref _closingBehavior, value);
    }

    [JsonProperty]
    public string TransEngine
    {
        get => _transEngine ?? "AiModel";
        set
        {
            var newValue = value ?? "AiModel";
            this.RaiseAndSetIfChanged(ref _transEngine, newValue);
        }
    }

    [JsonProperty]
    public string UsingAiModel
    {
        get => _usingAiModel ?? "OpenAI";
        set => this.RaiseAndSetIfChanged(ref _usingAiModel, value ?? "OpenAI");
    }

    [JsonProperty]
    public string? UsingAiModelId
    {
        get => _usingAiModelId;
        set => this.RaiseAndSetIfChanged(ref _usingAiModelId, value);
    }

    [JsonProperty]
    public string UsingMachineTrans
    {
        get => _usingMachineTrans ?? "Baidu";
        set => this.RaiseAndSetIfChanged(ref _usingMachineTrans, value ?? "Baidu");
    }
}