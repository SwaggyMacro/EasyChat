using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

[JsonObject(MemberSerialization.OptIn)]
public class SpeechRecognitionConfig : ReactiveObject
{
    private string _recognitionLanguage = "";

    [JsonProperty]
    public string RecognitionLanguage
    {
        get => _recognitionLanguage;
        set => this.RaiseAndSetIfChanged(ref _recognitionLanguage, value);
    }

    private bool _isTranslationEnabled;

    [JsonProperty]
    public bool IsTranslationEnabled
    {
        get => _isTranslationEnabled;
        set => this.RaiseAndSetIfChanged(ref _isTranslationEnabled, value);
    }

    private bool _isRealTimePreviewEnabled;

    [JsonProperty]
    public bool IsRealTimePreviewEnabled
    {
        get => _isRealTimePreviewEnabled;
        set => this.RaiseAndSetIfChanged(ref _isRealTimePreviewEnabled, value);
    }

    private string _targetLanguage = "";

    [JsonProperty]
    public string TargetLanguage
    {
        get => _targetLanguage;
        set => this.RaiseAndSetIfChanged(ref _targetLanguage, value);
    }

    private string _engineId = "";

    [JsonProperty]
    public string EngineId
    {
        get => _engineId;
        set => this.RaiseAndSetIfChanged(ref _engineId, value);
    }
}
