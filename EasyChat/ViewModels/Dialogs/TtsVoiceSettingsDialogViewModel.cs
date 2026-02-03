using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Avalonia.Controls.Notifications;
using EasyChat.Models.Configuration;
using EasyChat.Services.Abstractions;
using EasyChat.Services.Languages;
using EasyChat.Services.Speech.Tts;
using ReactiveUI;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace EasyChat.ViewModels.Dialogs;

public class ConfiguredVoiceItem
{
    public required LanguageDefinition Language { get; set; }
    public required string VoiceId { get; set; }
    public string VoiceName { get; set; } = "Unknown Voice";
    public string VoiceLocale { get; set; } = "";
}

public class TtsVoiceSettingsDialogViewModel : ViewModelBase
{
    private readonly ISukiDialogManager _dialogManager;
    private readonly ISukiToastManager _toastManager;
    private readonly ISukiDialog _dialog;
    private readonly ITtsService _ttsService;
    private readonly TtsConfig _ttsConfig;
    private readonly TtsManager? _ttsManager;
    
    private List<TtsVoiceDefinition> _allVoices = new();

    public List<string> AvailableProviders { get; private set; }

    public string SelectedProvider
    {
        get => _ttsConfig.Provider;
        set
        {
            if (_ttsConfig.Provider != value)
            {
                _ttsConfig.Provider = value;
                this.RaisePropertyChanged();
                
                _ttsManager?.SwitchProvider(value);
                Initialize(); // Reload voices for new provider
            }
        }
    }

    public ObservableCollection<ConfiguredVoiceItem> ConfiguredVoices
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = [];

    public ConfiguredVoiceItem? SelectedConfiguredVoice
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<Unit, Unit> AddCommand { get; }
    public ReactiveCommand<ConfiguredVoiceItem, Unit> EditCommand { get; }
    public ReactiveCommand<ConfiguredVoiceItem, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public TtsVoiceSettingsDialogViewModel(ISukiDialogManager dialogManager, ISukiToastManager toastManager, ISukiDialog dialog, ITtsService ttsService, TtsConfig ttsConfig) 
    {
        _dialogManager = dialogManager;
        _toastManager = toastManager;
        _dialog = dialog;
        _ttsService = ttsService;
        _ttsConfig = ttsConfig;

        if (_ttsService is TtsManager manager)
        {
            _ttsManager = manager;
            AvailableProviders = _ttsManager.GetAvailableProviders();
        }
        else
        {
            AvailableProviders = new List<string> { _ttsService.ProviderId };
        }
        
        AddCommand = ReactiveCommand.Create(AddVoiceMapping);
        EditCommand = ReactiveCommand.Create<ConfiguredVoiceItem>(EditVoiceMapping);
        DeleteCommand = ReactiveCommand.Create<ConfiguredVoiceItem>(DeleteVoiceMapping);
        CloseCommand = ReactiveCommand.Create(Close);

        if (string.IsNullOrEmpty(_ttsConfig.Provider))
        {
            _ttsConfig.Provider = _ttsService.ProviderId;
        }

        Initialize();
    }

    private void Initialize()
    {
        _allVoices = _ttsService.GetVoices();
        RefreshConfiguredVoices();
    }

    private void RefreshConfiguredVoices()
    {
        var list = new List<ConfiguredVoiceItem>();
        
        if (_ttsConfig.ProviderVoicePreferences.TryGetValue(SelectedProvider, out var prefs))
        {
            foreach (var kvp in prefs)
            {
                var langId = kvp.Key;
                var voiceId = kvp.Value;
                var lang = LanguageService.GetLanguage(langId);
                var voice = _allVoices.FirstOrDefault(v => v.Id == voiceId);

                list.Add(new ConfiguredVoiceItem
                {
                    Language = lang,
                    VoiceId = voiceId,
                    VoiceName = voice?.Name ?? voiceId,
                    VoiceLocale = voice?.LanguageId ?? "?"
                });
            }
        }
        
        ConfiguredVoices = new ObservableCollection<ConfiguredVoiceItem>(list.OrderBy(x => x.Language.EnglishName));
    }

    private void AddVoiceMapping()
    {
        try 
        {
            _dialog.Dismiss();
            
            _dialogManager.CreateDialog()
                .WithTitle(Lang.Resources.Tts_AddVoiceMapping)
                .WithViewModel(d => new TtsEditVoiceDialogViewModel(
                    d, 
                    _dialogManager, 
                    _ttsService, 
                    _toastManager, 
                    _allVoices)
                {
                    OnSave = (lang, voice) =>
                    {
                         _ttsConfig.SetVoiceForLanguage(SelectedProvider, lang.Id, voice.Id);
                         ShowSettingsDialog();
                    },
                    OnCancel = () => ShowSettingsDialog()
                })
                .TryShow();
        }
        catch (Exception ex)
        {
            _toastManager.CreateToast()
                .WithTitle(Lang.Resources.Tts_ErrorOpeningDialog)
                .WithContent(ex.Message)
                .OfType(NotificationType.Error)
                .Queue();
                
            ShowSettingsDialog();
        }
    }

    private void EditVoiceMapping(ConfiguredVoiceItem item)
    {
        _dialog.Dismiss();

        _dialogManager.CreateDialog()
            .WithTitle(Lang.Resources.Tts_EditVoiceMapping)
            .WithViewModel(d => new TtsEditVoiceDialogViewModel(
                d, 
                _dialogManager, 
                _ttsService, 
                _toastManager, 
                _allVoices, 
                item.Language, 
                item.VoiceId)
            {
                OnSave = (lang, voice) =>
                {
                    if (lang.Id != item.Language.Id)
                    {
                         _ttsConfig.RemoveVoiceForLanguage(SelectedProvider, item.Language.Id);
                    }
                    
                    _ttsConfig.SetVoiceForLanguage(SelectedProvider, lang.Id, voice.Id);
                    ShowSettingsDialog();
                },
                OnCancel = () => ShowSettingsDialog()
            })
            .TryShow();
    }
    
    private void ShowSettingsDialog()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            _dialogManager.CreateDialog()
            .WithTitle(Lang.Resources.Tts_Configuration)
            .WithViewModel(dialog => new TtsVoiceSettingsDialogViewModel(
                _dialogManager, 
                _toastManager,
                dialog,
                _ttsService, 
                _ttsConfig))
            .TryShow();
        });
    }

    private void DeleteVoiceMapping(ConfiguredVoiceItem item)
    {
         _ttsConfig.RemoveVoiceForLanguage(SelectedProvider, item.Language.Id);
        RefreshConfiguredVoices();
    }


    public void Close()
    {
        _dialog.Dismiss();
    }
}
