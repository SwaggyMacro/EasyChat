using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using EasyChat.Constants;
using EasyChat.Lang;
using EasyChat.Models.Configuration;
using ReactiveUI;
using SukiUI.Dialogs;

namespace EasyChat.ViewModels.AiModels;

public class AiModelEditDialogViewModel : ViewModelBase
{
    private readonly ISukiDialog _dialog;
    private readonly CustomAiModel? _existingModel;

    private string _apiKey = string.Empty;

    private string _apiUrl = string.Empty;

    private string _model = string.Empty;

    private string _name = string.Empty;

    private AiModelType _selectedModelType = AiModelType.OpenAi;
    
    private bool _useProxy;

    public AiModelEditDialogViewModel(ISukiDialog dialog, CustomAiModel? existingModel = null)
    {
        _dialog = dialog;
        _existingModel = existingModel;

        if (existingModel != null)
        {
            SelectedModelType = existingModel.ModelType;
            Name = existingModel.Name;
            ApiUrl = existingModel.ApiUrl;
            ApiKey = existingModel.ApiKey;
            Model = existingModel.Model;
            UseProxy = existingModel.UseProxy;
        }
        else
        {
            UpdateDefaultsForModelType(AiModelType.OpenAi);
        }

        var canSave = this.WhenAnyValue(
            x => x.ApiUrl,
            x => x.Model,
            x => x.Name,
            x => x.SelectedModelType,
            (url, model, name, type) =>
            {
                if (string.IsNullOrWhiteSpace(url)) return false;
                if (string.IsNullOrWhiteSpace(model)) return false;
                if (type == AiModelType.Custom && string.IsNullOrWhiteSpace(name)) return false;
                return true;
            });

        SaveCommand = ReactiveCommand.Create(() =>
        {
            OnClose?.Invoke(GetResult());
            _dialog.Dismiss();
        }, canSave);

        CancelCommand = ReactiveCommand.Create(() =>
        {
            OnClose?.Invoke(null);
            _dialog.Dismiss();
        });
    }

    public string Title => _existingModel == null ? Resources.AddModel : Resources.EditModel;
    public string ButtonText => _existingModel == null ? Resources.Add : Resources.Save;

    public List<AiModelType> AvailableModelTypes { get; } =
        Enum.GetValues<AiModelType>().ToList();

    public AiModelType SelectedModelType
    {
        get => _selectedModelType;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedModelType, value);
            UpdateDefaultsForModelType(value);
            this.RaisePropertyChanged(nameof(AvailableModels));
            this.RaisePropertyChanged(nameof(IsCustomModel));
            this.RaisePropertyChanged(nameof(DisplayName));
        }
    }

    public bool IsCustomModel => SelectedModelType == AiModelType.Custom;

    public string DisplayName => SelectedModelType switch
    {
        AiModelType.OpenAi => "OpenAI",
        AiModelType.Gemini => "Gemini",
        AiModelType.Claude => "Claude",
        AiModelType.Custom => Resources.CustomModel,
        _ => Resources.Unknown
    };

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public string ApiUrl
    {
        get => _apiUrl;
        set => this.RaiseAndSetIfChanged(ref _apiUrl, value);
    }

    public string ApiKey
    {
        get => _apiKey;
        set => this.RaiseAndSetIfChanged(ref _apiKey, value);
    }

    public string Model
    {
        get => _model;
        set => this.RaiseAndSetIfChanged(ref _model, value);
    }
    
    public bool UseProxy
    {
        get => _useProxy;
        set => this.RaiseAndSetIfChanged(ref _useProxy, value);
    }

    public List<string> AvailableModels => SelectedModelType switch
    {
        AiModelType.OpenAi => ModelList.OpenAiModels,
        AiModelType.Gemini => ModelList.GeminiModels,
        AiModelType.Claude => ModelList.ClaudeModels,
        _ => new List<string>()
    };

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public Action<CustomAiModel?>? OnClose { get; set; }

    private void UpdateDefaultsForModelType(AiModelType modelType)
    {
        switch (modelType)
        {
            case AiModelType.OpenAi:
                ApiUrl = "https://api.openai.com/v1";
                if (string.IsNullOrEmpty(Model) || !ModelList.OpenAiModels.Contains(Model))
                    Model = "gpt-4o";
                Name = "OpenAI";
                break;
            case AiModelType.Gemini:
                ApiUrl = "https://generativelanguage.googleapis.com/v1beta/openai/";
                if (string.IsNullOrEmpty(Model) || !ModelList.GeminiModels.Contains(Model))
                    Model = "gemini-pro";
                Name = "Gemini";
                break;
            case AiModelType.Claude:
                ApiUrl = "https://api.anthropic.com/v1/";
                if (string.IsNullOrEmpty(Model) || !ModelList.ClaudeModels.Contains(Model))
                    Model = "claude-3-opus-20240229";
                Name = "Claude";
                break;
            case AiModelType.Custom:
                ApiUrl = "https://api.openai.com/v1";
                Model = "";
                Name = "";
                break;
        }
    }

    public CustomAiModel GetResult()
    {
        return new CustomAiModel
        {
            Id = _existingModel?.Id ?? Guid.NewGuid().ToString(),
            Name = string.IsNullOrWhiteSpace(Name) ? DisplayName : Name,
            ModelType = SelectedModelType,
            ApiKey = ApiKey,
            ApiUrl = ApiUrl,
            Model = Model,
            UseProxy = UseProxy
        };
    }
}