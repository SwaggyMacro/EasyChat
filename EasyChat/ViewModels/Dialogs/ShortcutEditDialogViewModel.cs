// Placeholder to ensure I look at the view first

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using EasyChat.Lang;
using EasyChat.Models.Configuration;
using EasyChat.Services.Abstractions;
using EasyChat.Services.Languages;
using Material.Icons;
using ReactiveUI;
using SukiUI.Dialogs;

namespace EasyChat.ViewModels.Dialogs;

public class ShortcutEditDialogViewModel : ViewModelBase
{
    private readonly ISukiDialog _dialog;
    private readonly ShortcutEntry? _existingEntry;
    private readonly IConfigurationService _configurationService;

    private bool _isRecording;

    private string _keyCombination = "";

    private string _parameter = "";

    private IShortcutActionDefinition _selectedAction = ShortcutActionDefinition.AvailableActions.First();

    private IEnumerable<string>? _availableParameterOptions;

    public class EngineOption
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public bool IsMachine { get; set; }

        public override string ToString() => Name;
        
        public override bool Equals(object? obj)
        {
            if (obj is EngineOption other)
            {
                return Id == other.Id;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }
    }

    public bool IsComplexSwitchAction => SelectedAction?.ActionType == "SwitchEngineSourceTarget";

    private EngineOption? _selectedEngineOption;
    private LanguageDefinition _selectedSourceLang;
    private LanguageDefinition _selectedTargetLang;
    private List<LanguageDefinition> _availableLanguages;

    public List<EngineOption> AvailableEngineOptions { get; }

    public List<LanguageDefinition> AvailableLanguages
    {
        get => _availableLanguages;
        set => this.RaiseAndSetIfChanged(ref _availableLanguages, value);
    }

    public EngineOption SelectedEngineOption
    {
        get => _selectedEngineOption;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedEngineOption, value);
            UpdateAvailableLanguages();
        }
    }

    public LanguageDefinition SelectedSourceLang
    {
        get => _selectedSourceLang;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSourceLang, value);
        }
    }

    public LanguageDefinition SelectedTargetLang
    {
        get => _selectedTargetLang;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTargetLang, value);
        }
    }

    public ShortcutEditDialogViewModel(ISukiDialog dialog, IConfigurationService configurationService, string[] allowedActionTypes, ShortcutEntry? existingEntry = null)
    {
        _dialog = dialog;
        _configurationService = configurationService;
        _existingEntry = existingEntry;

        AvailableActions = ShortcutActionDefinition.AvailableActions
            .Where(a => allowedActionTypes.Contains(a.ActionType))
            .ToArray();

        AvailableEngineOptions = new List<EngineOption>();
        
        // Add Machine Engines
        foreach (var provider in MachineTrans.SupportedProviders)
        {
            AvailableEngineOptions.Add(new EngineOption { Name = provider, Id = provider, IsMachine = true });
        }
        
        // Add AI Models
        if (_configurationService.AiModel?.ConfiguredModels != null)
        {
            foreach (var model in _configurationService.AiModel.ConfiguredModels)
            {
                AvailableEngineOptions.Add(new EngineOption { Name = model.Name, Id = model.Id, IsMachine = false });
            }
        }

        // defaults
        _selectedEngineOption = AvailableEngineOptions.FirstOrDefault(e => e.Id == "Baidu") ?? AvailableEngineOptions.FirstOrDefault();
        
        // Initialize languages based on default engine
        UpdateAvailableLanguages();
        
        _selectedSourceLang = AvailableLanguages.FirstOrDefault(l => l.Id == "auto") ?? AvailableLanguages.FirstOrDefault();
        _selectedTargetLang = AvailableLanguages.FirstOrDefault(l => l.Id == "zh-Hans") ?? AvailableLanguages.FirstOrDefault();

        // Ensure we have at least one action if possible, or matches existing
        if (existingEntry != null)
        {
            var def = ShortcutActionDefinition.GetByType(existingEntry.ActionType);
            if (def != null) SelectedAction = def;
            KeyCombination = existingEntry.KeyCombination;

            if (IsComplexSwitchAction && existingEntry.Parameter != null)
            {
                var param = existingEntry.Parameter;
                
                // Try to find by ID first
                var engInfo = AvailableEngineOptions.FirstOrDefault(e => e.Id == param.EngineId);
                
                // Fallback to Name
                if (engInfo == null)
                {
                    engInfo = AvailableEngineOptions.FirstOrDefault(e => e.Name == param.Engine);
                }
                
                if (engInfo != null) 
                {
                    _selectedEngineOption = engInfo;
                    UpdateAvailableLanguages(); 
                }

                if (param.Source != null)
                {
                    var src = AvailableLanguages.FirstOrDefault(l => l.Id == param.Source.Id);
                    if (src != null) _selectedSourceLang = src;
                }

                if (param.Target != null)
                {
                    var tgt = AvailableLanguages.FirstOrDefault(l => l.Id == param.Target.Id);
                    if (tgt != null) _selectedTargetLang = tgt;
                }
            }
            else
            {
                 Parameter = existingEntry.Parameter?.Value ?? "";
            }
        }
        else if (AvailableActions.Any())
        {
            SelectedAction = AvailableActions.First();
        }

        UpdateAvailableParameterOptions();

        ToggleRecordingCommand = ReactiveCommand.Create(() =>
        {
            IsRecording = !IsRecording;
            if (IsRecording)
                KeyCombination = "";
            else if (string.IsNullOrEmpty(KeyCombination) && _existingEntry != null)
                // Restore if cancelled/empty
                KeyCombination = _existingEntry.KeyCombination;
        });

        var canSave = this.WhenAnyValue(
            x => x.KeyCombination,
            x => x.SelectedAction,
            x => x.Parameter,
            x => x.SelectedEngineOption,
            (key, action, param, option) =>
            {
                if (string.IsNullOrEmpty(key)) return false;
                if (!action.RequiresParameter) return true;
                
                if (IsComplexSwitchAction)
                {
                    // For complex action, check selected engine/langs instead of param string
                    return option != null && _selectedSourceLang != null && _selectedTargetLang != null;
                }

                return !string.IsNullOrWhiteSpace(param);
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

    public string Title => _existingEntry == null
        ? $"{Resources.Add} {Resources.Shortcut}"
        : $"{Resources.Edit} {Resources.Shortcut}";

    public string ButtonText => _existingEntry == null ? Resources.Add : Resources.Save;
    public MaterialIconKind Icon => _existingEntry == null ? MaterialIconKind.Plus : MaterialIconKind.Edit;

    public IShortcutActionDefinition SelectedAction
    {
        get => _selectedAction;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedAction, value);
            this.RaisePropertyChanged(nameof(IsComplexSwitchAction));
            UpdateAvailableParameterOptions();
            
            if (_existingEntry == null || _existingEntry.ActionType != value.ActionType)
            {
                Parameter = "";
            }
        }
    }

    public string Parameter
    {
        get => _parameter;
        set => this.RaiseAndSetIfChanged(ref _parameter, value);
    }

    public IEnumerable<string>? AvailableParameterOptions
    {
        get => _availableParameterOptions;
        set => this.RaiseAndSetIfChanged(ref _availableParameterOptions, value);
    }

    public string KeyCombination
    {
        get => _keyCombination;
        set => this.RaiseAndSetIfChanged(ref _keyCombination, value);
    }

    public bool IsRecording
    {
        get => _isRecording;
        set => this.RaiseAndSetIfChanged(ref _isRecording, value);
    }

    public IShortcutActionDefinition[] AvailableActions { get; }

    public ReactiveCommand<Unit, Unit> ToggleRecordingCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public Action<ShortcutEntry?>? OnClose { get; set; }

    public ShortcutEntry GetResult()
    {
        return new ShortcutEntry
        {
            ActionType = SelectedAction.ActionType,
            Parameter = GetShortcutParameter(),
            KeyCombination = KeyCombination,
            IsEnabled = _existingEntry?.IsEnabled ?? true
        };
    }

    private ShortcutParameter GetShortcutParameter()
    {
        if (IsComplexSwitchAction)
        {
            return new ShortcutParameter
            {
                Engine = SelectedEngineOption?.Name ?? "",
                EngineId = SelectedEngineOption?.Id,
                Source = SelectedSourceLang,
                Target = SelectedTargetLang
            };
        }

        return new ShortcutParameter
        {
            Value = Parameter
        };
    }

    private void UpdateAvailableParameterOptions()
    {
        AvailableParameterOptions = SelectedAction.GetParameterOptions(_configurationService);
    }



    private void UpdateAvailableLanguages()
    {
        var allLanguages = Services.Languages.LanguageService.GetAllLanguages();

        // Safe check for null
        if (SelectedEngineOption == null)
        {
             AvailableLanguages = allLanguages.ToList();
             return;
        }

        if (SelectedEngineOption.IsMachine)
        {
            AvailableLanguages = allLanguages
                .Where(l => l.Id == "auto" || !string.IsNullOrEmpty(l.GetCode(SelectedEngineOption.Id)))
                .ToList();
        }
        else
        {
            // AI Model - assume all valid
            AvailableLanguages = allLanguages.ToList();
        }

        // Re-validate selections
        var currentSourceId = SelectedSourceLang?.Id ?? "auto";
        var currentTargetId = SelectedTargetLang?.Id ?? "zh-Hans";

        var newSource = AvailableLanguages.FirstOrDefault(l => l.Id == currentSourceId);
        SelectedSourceLang = newSource ?? AvailableLanguages.FirstOrDefault(l => l.Id == "auto") ?? AvailableLanguages.FirstOrDefault()!;

        var newTarget = AvailableLanguages.FirstOrDefault(l => l.Id == currentTargetId);
        SelectedTargetLang = newTarget ?? AvailableLanguages.FirstOrDefault(l => l.Id == "zh-Hans") ?? AvailableLanguages.FirstOrDefault()!;
    }
}