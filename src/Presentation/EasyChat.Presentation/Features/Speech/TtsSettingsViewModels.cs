using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Speech;
using EasyChat.Presentation.Lang;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Foundation.Localization;
using EasyChat.Presentation.Foundation.Navigation;
using EasyChat.Presentation.Foundation.UiHost;
using ReactiveUI;

namespace EasyChat.Presentation.Features.Speech;

public sealed class TtsLanguageItem(TtsLanguage value)
{
    public TtsLanguage Value { get; } = value;
    public string Id => Value.Locale;
    public string EnglishName => Value.EnglishName;
    public string ChineseName => Value.ChineseName;
    public string Icon => Value.Icon;
    public string DisplayName => LanguageDisplayNames.ForUi(ChineseName, EnglishName);
}

public sealed class ConfiguredVoiceItem
{
    public required TtsLanguageItem Language { get; init; }
    public required string VoiceId { get; init; }
    public required string VoiceName { get; init; }
    public required string VoiceLocale { get; init; }
}

public sealed class TtsVoiceSettingsDialogViewModel : ConventionViewModelBase
{
    private readonly IUiDialogHost _dialogHost;
    private readonly IUiDialogSession _dialog;
    private readonly IUiToastHost _toasts;
    private readonly ITtsUseCases _tts;
    private readonly LiveTtsSettings _settings;
    private IReadOnlyList<TtsVoice> _voices = [];
    private IReadOnlyList<TtsLanguageItem> _languages = [];
    private ObservableCollection<ConfiguredVoiceItem> _configuredVoices = [];
    private ConfiguredVoiceItem? _selectedConfiguredVoice;
    private string _selectedProvider;

    public TtsVoiceSettingsDialogViewModel(
        IUiDialogHost dialogHost,
        IUiDialogSession dialog,
        IUiToastHost toasts,
        ITtsUseCases tts,
        LiveTtsSettings settings)
    {
        _dialogHost = dialogHost;
        _dialog = dialog;
        _toasts = toasts;
        _tts = tts;
        _settings = settings;
        AvailableProviders = tts.GetProviders().Select(provider => provider.Id).ToArray();
        _selectedProvider = AvailableProviders.Contains(settings.Provider, StringComparer.Ordinal)
            ? settings.Provider
            : AvailableProviders.FirstOrDefault() ?? TtsProviderIds.EdgeTts;
        AddCommand = ReactiveCommand.Create(AddVoiceMapping);
        EditCommand = ReactiveCommand.Create<ConfiguredVoiceItem>(EditVoiceMapping);
        DeleteCommand = ReactiveCommand.Create<ConfiguredVoiceItem>(DeleteVoiceMapping);
        CloseCommand = ReactiveCommand.Create(dialog.Dismiss);
        _ = LoadAsync();
    }

    public IReadOnlyList<string> AvailableProviders { get; }
    public string SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (_selectedProvider == value)
                return;
            this.RaiseAndSetIfChanged(ref _selectedProvider, value);
            _settings.Provider = value;
            _ = LoadAsync();
        }
    }

    public ObservableCollection<ConfiguredVoiceItem> ConfiguredVoices
    {
        get => _configuredVoices;
        private set => this.RaiseAndSetIfChanged(ref _configuredVoices, value);
    }

    public ConfiguredVoiceItem? SelectedConfiguredVoice
    {
        get => _selectedConfiguredVoice;
        set => this.RaiseAndSetIfChanged(ref _selectedConfiguredVoice, value);
    }

    public ReactiveCommand<Unit, Unit> AddCommand { get; }
    public ReactiveCommand<ConfiguredVoiceItem, Unit> EditCommand { get; }
    public ReactiveCommand<ConfiguredVoiceItem, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    private async Task LoadAsync()
    {
        var voices = await _tts.GetVoicesAsync(SelectedProvider);
        var languages = await _tts.GetLanguagesAsync(SelectedProvider);
        if (voices.IsFailure || languages.IsFailure)
        {
            ShowError(voices.IsFailure ? voices.Error.Message : languages.Error.Message);
            return;
        }

        _voices = voices.Value;
        _languages = languages.Value.Select(language => new TtsLanguageItem(language)).ToArray();
        RefreshConfiguredVoices();
    }

    private void RefreshConfiguredVoices()
    {
        var configured = _settings.ProviderVoicePreferences.TryGetValue(SelectedProvider, out var preferences)
            ? preferences
            : [];
        ConfiguredVoices = new ObservableCollection<ConfiguredVoiceItem>(configured.Select(preference =>
        {
            var language = _languages.FirstOrDefault(item =>
                               string.Equals(item.Id, preference.Key, StringComparison.OrdinalIgnoreCase))
                           ?? new TtsLanguageItem(new TtsLanguage(
                               preference.Key, preference.Key, string.Empty,
                               preference.Key, string.Empty, "unknown.png"));
            var voice = _voices.FirstOrDefault(item => item.Id == preference.Value);
            return new ConfiguredVoiceItem
            {
                Language = language,
                VoiceId = preference.Value,
                VoiceName = voice?.Name ?? preference.Value,
                VoiceLocale = voice?.LanguageId ?? "?"
            };
        }).OrderBy(item => item.Language.EnglishName));
    }

    private void AddVoiceMapping()
    {
        _dialog.Dismiss();
        ShowEditor(null);
    }

    private void EditVoiceMapping(ConfiguredVoiceItem? item)
    {
        if (item is null)
            return;
        _dialog.Dismiss();
        ShowEditor(item);
    }

    private void ShowEditor(ConfiguredVoiceItem? current)
    {
        _dialogHost.ShowContent(new UiContentDialogOptions
        {
            Title = current is null ? Resources.Tts_AddVoiceMapping : Resources.Tts_EditVoiceMapping,
            CreateContent = session => new TtsEditVoiceDialogViewModel(
                session,
                _dialogHost,
                _tts,
                SelectedProvider,
                _languages,
                _voices,
                current?.Language,
                current?.VoiceId)
            {
                OnSave = (language, voice) =>
                {
                    if (current is not null && current.Language.Id != language.Id)
                        _settings.RemoveVoiceForLanguage(SelectedProvider, current.Language.Id);
                    _settings.SetVoiceForLanguage(SelectedProvider, language.Id, voice.Id);
                    ReopenSettings();
                },
                OnCancel = ReopenSettings
            }
        });
    }

    private void DeleteVoiceMapping(ConfiguredVoiceItem? item)
    {
        if (item is null)
            return;
        _settings.RemoveVoiceForLanguage(SelectedProvider, item.Language.Id);
        RefreshConfiguredVoices();
    }

    private void ReopenSettings() => _dialogHost.ShowContent(new UiContentDialogOptions
    {
        Title = Resources.Tts_Configuration,
        CreateContent = session => new TtsVoiceSettingsDialogViewModel(
            _dialogHost, session, _toasts, _tts, _settings)
    });

    private void ShowError(string message) =>
        _toasts.Show(Resources.Tts_ErrorOpeningDialog, message, UiMessageSeverity.Error);
}

public sealed class TtsEditVoiceDialogViewModel : ConventionViewModelBase
{
    private readonly IUiDialogSession _dialog;
    private readonly IUiDialogHost _dialogHost;
    private readonly ITtsUseCases _tts;
    private readonly string _provider;
    private readonly IReadOnlyList<TtsVoice> _allVoices;
    private TtsLanguageItem? _selectedLanguage;
    private TtsVoice? _selectedVoice;
    private string _searchText = string.Empty;
    private ObservableCollection<TtsVoice> _filteredVoices = [];

    public TtsEditVoiceDialogViewModel(
        IUiDialogSession dialog,
        IUiDialogHost dialogHost,
        ITtsUseCases tts,
        string provider,
        IReadOnlyList<TtsLanguageItem> languages,
        IReadOnlyList<TtsVoice> voices,
        TtsLanguageItem? initialLanguage = null,
        string? initialVoiceId = null)
    {
        _dialog = dialog;
        _dialogHost = dialogHost;
        _tts = tts;
        _provider = provider;
        AvailableLanguages = languages;
        _allVoices = voices;
        _selectedLanguage = initialLanguage ?? languages.FirstOrDefault(language =>
            language.Id.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        FilterVoices();
        _selectedVoice = voices.FirstOrDefault(voice => voice.Id == initialVoiceId);
        SaveCommand = ReactiveCommand.Create(
            Save,
            this.WhenAnyValue(
                viewModel => viewModel.SelectedLanguage,
                viewModel => viewModel.SelectedVoice,
                (language, voice) => language is not null && voice is not null));
        CancelCommand = ReactiveCommand.Create(Cancel);
        PreviewCommand = ReactiveCommand.Create(
            Preview,
            this.WhenAnyValue(viewModel => viewModel.SelectedVoice).Select(voice => voice is not null));
    }

    public IReadOnlyList<TtsLanguageItem> AvailableLanguages { get; }
    public TtsLanguageItem? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
            FilterVoices();
        }
    }
    public ObservableCollection<TtsVoice> FilteredVoices { get => _filteredVoices; private set => this.RaiseAndSetIfChanged(ref _filteredVoices, value); }
    public TtsVoice? SelectedVoice { get => _selectedVoice; set => this.RaiseAndSetIfChanged(ref _selectedVoice, value); }
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
    public Action<TtsLanguageItem, TtsVoice>? OnSave { get; init; }
    public Action? OnCancel { get; init; }

    private void FilterVoices()
    {
        var search = SearchText.Trim();
        var locale = SelectedLanguage?.Id;
        var voices = _allVoices.Where(voice =>
            (string.IsNullOrWhiteSpace(locale) || voice.LanguageId.StartsWith(locale, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(search) ||
             voice.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             voice.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             voice.LanguageId.Contains(search, StringComparison.OrdinalIgnoreCase)));
        FilteredVoices = new ObservableCollection<TtsVoice>(voices);
        if (SelectedVoice is not null && !FilteredVoices.Contains(SelectedVoice))
            SelectedVoice = null;
    }

    private void Save()
    {
        if (SelectedLanguage is not null && SelectedVoice is not null)
            OnSave?.Invoke(SelectedLanguage, SelectedVoice);
        _dialog.Dismiss();
    }

    private void Cancel()
    {
        OnCancel?.Invoke();
        _dialog.Dismiss();
    }

    private void Preview()
    {
        if (SelectedVoice is null)
            return;
        _dialog.Dismiss();
        _dialogHost.ShowContent(new UiContentDialogOptions
        {
            CreateContent = session => new TtsPreviewInputDialogViewModel(
                session, _tts, _provider, SelectedVoice.Id)
            {
                OnDismiss = Reopen
            }
        });
    }

    private void Reopen() => _dialogHost.ShowContent(new UiContentDialogOptions
    {
        Title = Resources.Tts_EditVoiceMapping,
        CreateContent = session => new TtsEditVoiceDialogViewModel(
            session, _dialogHost, _tts, _provider,
            AvailableLanguages, _allVoices, SelectedLanguage, SelectedVoice?.Id)
        {
            SearchText = SearchText,
            OnSave = OnSave,
            OnCancel = OnCancel
        }
    });
}

public sealed class TtsPreviewInputDialogViewModel : ConventionViewModelBase
{
    private readonly IUiDialogSession _dialog;
    private readonly ITtsUseCases _tts;
    private readonly string _provider;
    private readonly string _voiceId;
    private string _inputText = Resources.Tts_PreviewDefaultText;
    private bool _isPlaying;

    public TtsPreviewInputDialogViewModel(
        IUiDialogSession dialog,
        ITtsUseCases tts,
        string provider,
        string voiceId)
    {
        _dialog = dialog;
        _tts = tts;
        _provider = provider;
        _voiceId = voiceId;
        PlayCommand = ReactiveCommand.CreateFromTask(
            PlayAsync,
            this.WhenAnyValue(viewModel => viewModel.IsPlaying).Select(playing => !playing));
        CloseCommand = ReactiveCommand.Create(Close);
    }

    public string InputText { get => _inputText; set => this.RaiseAndSetIfChanged(ref _inputText, value); }
    public bool IsPlaying { get => _isPlaying; private set => this.RaiseAndSetIfChanged(ref _isPlaying, value); }
    public ReactiveCommand<Unit, Unit> PlayCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public Action? OnDismiss { get; init; }

    private async Task PlayAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText))
            return;
        IsPlaying = true;
        try
        {
            await _tts.EnqueueAsync(
                new TtsSynthesisRequest(InputText, _voiceId, _provider),
                interruptCurrent: true);
        }
        finally
        {
            IsPlaying = false;
        }
    }

    private void Close()
    {
        OnDismiss?.Invoke();
        _dialog.Dismiss();
    }
}
