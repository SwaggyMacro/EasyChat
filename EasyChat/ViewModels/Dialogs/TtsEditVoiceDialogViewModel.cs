using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using EasyChat.Services.Abstractions;
using EasyChat.Services.Languages;
using EasyChat.Services.Speech.Tts;
using ReactiveUI;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace EasyChat.ViewModels.Dialogs;

public class TtsEditVoiceDialogViewModel : ViewModelBase
{
    private readonly ISukiDialog _dialog;
    private readonly ISukiDialogManager _dialogManager;
    private readonly ITtsService _ttsService;
    private readonly ISukiToastManager _toastManager;
    
    public List<LanguageDefinition> AvailableLanguages { get; }

    private LanguageDefinition? _selectedLanguage;
    public LanguageDefinition? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
            FilterVoices();
        }
    }

    private List<TtsVoiceDefinition> _allVoices;
    
    private ObservableCollection<TtsVoiceDefinition> _filteredVoices = new();
    public ObservableCollection<TtsVoiceDefinition> FilteredVoices
    {
        get => _filteredVoices;
        set => this.RaiseAndSetIfChanged(ref _filteredVoices, value);
    }

    private TtsVoiceDefinition? _selectedVoice;
    public TtsVoiceDefinition? SelectedVoice
    {
        get => _selectedVoice;
        set => this.RaiseAndSetIfChanged(ref _selectedVoice, value);
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchText, value);
            FilterVoices();
        }
    }

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviewCommand { get; }
    
    public Action<LanguageDefinition, TtsVoiceDefinition>? OnSave { get; set; }
    public Action? OnCancel { get; set; }

    public TtsEditVoiceDialogViewModel(
        ISukiDialog dialog, 
        ISukiDialogManager dialogManager,
        ITtsService ttsService,
        ISukiToastManager toastManager,
        List<TtsVoiceDefinition> allVoices, 
        LanguageDefinition? initialLang = null, 
        string? initialVoiceId = null)
    {
        _dialog = dialog;
        _dialogManager = dialogManager;
        _ttsService = ttsService;
        _toastManager = toastManager;
        _allVoices = allVoices;
        AvailableLanguages = LanguageService.GetAllLanguages().OrderBy(x => x.EnglishName).ToList();
        
        _selectedLanguage = initialLang ?? AvailableLanguages.FirstOrDefault(l => l.Id == LanguageKeys.EnglishId);
        
        // Initial filtering needs to happen after _selectedLanguage is set
        FilterVoices();

        if (initialVoiceId != null)
        {
            SelectedVoice = _allVoices.FirstOrDefault(v => v.Id == initialVoiceId);
        }

        SaveCommand = ReactiveCommand.Create(Save, this.WhenAnyValue(x => x.SelectedLanguage, x => x.SelectedVoice, (l, v) => l != null && v != null));
        CancelCommand = ReactiveCommand.Create(Cancel);
        PreviewCommand = ReactiveCommand.Create(Preview, this.WhenAnyValue(x => x.SelectedVoice).Select(v => v != null));
    }

    private void Preview()
    {
        if (SelectedVoice == null) return;
        
        _dialog.Dismiss();
        
        _dialogManager.CreateDialog()
            .WithViewModel(d => new TtsPreviewInputDialogViewModel(d, _ttsService, SelectedVoice.Id)
            {
                OnDismiss = ReopenSelf
            })
            .TryShow();
    }
    
    private void ReopenSelf()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            _dialogManager.CreateDialog()
                .WithTitle(Lang.Resources.Tts_EditVoiceMapping)
                .WithViewModel(d => new TtsEditVoiceDialogViewModel(
                    d, 
                    _dialogManager, 
                    _ttsService, 
                    _toastManager, 
                    _allVoices, 
                    SelectedLanguage, 
                    SelectedVoice?.Id)
                {
                    OnSave = OnSave,
                    OnCancel = OnCancel,
                    SearchText = SearchText // Restore search text
                })
                .TryShow();
        });
    }

    private void FilterVoices()
    {
        var query = _searchText.ToLower();
        var langCodes = _selectedLanguage?.ProviderCodes.Values.ToList();
        
        // Handle special case for Auto or when no language is selected
        bool filterByLang = _selectedLanguage != null && _selectedLanguage.Id != LanguageKeys.AutoId;

        var result = _allVoices.Where(v => 
        {
            // Text Search Filter
            bool matchesText = string.IsNullOrEmpty(query) || 
                               v.Name.ToLower().Contains(query) || 
                               v.Id.ToLower().Contains(query) || 
                               v.LanguageId.ToLower().Contains(query);
            
            // Language Filter
            bool matchesLang = !filterByLang || 
                               (langCodes != null && langCodes.Any(c => !string.IsNullOrEmpty(c) && v.LanguageId.StartsWith(c, StringComparison.OrdinalIgnoreCase)));

            return matchesText && matchesLang;
        }).ToList();
        
        FilteredVoices = new ObservableCollection<TtsVoiceDefinition>(result);
        
        // Clear selection if not in filtered list
        if (SelectedVoice != null && !FilteredVoices.Contains(SelectedVoice))
        {
            SelectedVoice = null;
        }
    }

    private void Save()
    {
        if (SelectedLanguage != null && SelectedVoice != null)
        {
            OnSave?.Invoke(SelectedLanguage, SelectedVoice);
            _dialog.Dismiss();
        }
    }

    private void Cancel()
    {
        OnCancel?.Invoke();
        _dialog.Dismiss();
    }
}
