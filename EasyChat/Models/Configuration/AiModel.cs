using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

public enum AiModelType
{
    OpenAi,
    Gemini,
    Claude,
    Custom
}

[JsonObject(MemberSerialization.OptIn)]
public class CustomAiModel : ReactiveObject
{
    private string _apiUrl = string.Empty;
    private string _id = Guid.NewGuid().ToString();

    private string _model = string.Empty;

    private AiModelType _modelType = AiModelType.Custom;

    private string _name = string.Empty;
    
    private bool _useProxy;


    private bool _isTesting;

    public bool IsTesting
    {
        get => _isTesting;
        set => this.RaiseAndSetIfChanged(ref _isTesting, value);
    }

    [JsonProperty]
    public string Id
    {
        get => _id;
        set => this.RaiseAndSetIfChanged(ref _id, value);
    }

    [JsonProperty]
    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    [JsonProperty]
    public AiModelType ModelType
    {
        get => _modelType;
        set => this.RaiseAndSetIfChanged(ref _modelType, value);
    }

    [JsonProperty] public ObservableCollection<string> ApiKeys { get; set; } = new();

    public string ApiKey
    {
        get
        {
            if (ApiKeys.Count == 0) return string.Empty;
            return ApiKeys[Random.Shared.Next(ApiKeys.Count)];
        }
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            if (!ApiKeys.Contains(value))
            {
                ApiKeys.Add(value);
                this.RaisePropertyChanged();
            }
        }
    }

    [JsonProperty]
    public string ApiUrl
    {
        get => _apiUrl;
        set => this.RaiseAndSetIfChanged(ref _apiUrl, value);
    }

    [JsonProperty]
    public string Model
    {
        get => _model;
        set => this.RaiseAndSetIfChanged(ref _model, value);
    }
    
    [JsonProperty]
    public bool UseProxy
    {
        get => _useProxy;
        set => this.RaiseAndSetIfChanged(ref _useProxy, value);
    }

    public string IconPath => ModelType switch
    {
        AiModelType.OpenAi => "avares://EasyChat/Assets/Images/Engine/openai.png",
        AiModelType.Gemini => "avares://EasyChat/Assets/Images/Engine/gemini.png",
        AiModelType.Claude => "avares://EasyChat/Assets/Images/Engine/claude.png",
        _ => "avares://EasyChat/Assets/Images/Engine/custom.png"
    };
}

/// <summary>
///     Wrapper for model cards display - includes both actual models and "Add" button placeholder.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class ModelCardItem
{
    public CustomAiModel? Model { get; set; }
    public bool IsAddButton => Model == null;
    public bool IsModelCard => Model != null;

    // Pass-through properties for binding
    public string Name => Model?.Name ?? string.Empty;
    public AiModelType ModelType => Model?.ModelType ?? AiModelType.Custom;
    public string ApiUrl => Model?.ApiUrl ?? string.Empty;
    public string ModelName => Model?.Model ?? string.Empty;
    public bool UseProxy => Model?.UseProxy ?? false;
}

[JsonObject(MemberSerialization.OptIn)]
public class AiModel
{
    [JsonProperty] public ObservableCollection<CustomAiModel> ConfiguredModels { get; set; } = [];
}