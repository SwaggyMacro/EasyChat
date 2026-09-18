using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Speech;
using EasyChat.Presentation.Lang;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Foundation.Localization;
using EasyChat.Presentation.Foundation.Navigation;
using ReactiveUI;
using ShadUI;

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
    private readonly DialogManager _dialogManager;
    private readonly ToastManager _toasts;
    private readonly ITtsUseCases _tts;
    private readonly LiveTtsSettings _settings;
    private IReadOnlyList<TtsVoice> _voices = [];
    private IReadOnlyList<TtsLanguageItem> _languages = [];
    private ObservableCollection<ConfiguredVoiceItem> _configuredVoices = [];
    private ConfiguredVoiceItem? _selectedConfiguredVoice;
    private string _selectedProvider;

    public TtsVoiceSettingsDialogViewModel(
        DialogManager dialogManager,
        ToastManager toasts,
        ITtsUseCases tts,
        LiveTtsSettings settings)
    {
        _dialogManager = dialogManager;
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
        CloseCommand = ReactiveCommand.Create(() => dialogManager.Close(this));
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
                           ?? _languages.FirstOrDefault(item =>
                               string.Equals(
                                   GetPrimaryLanguage(item.Id),
                                   GetPrimaryLanguage(preference.Key),
                                   StringComparison.OrdinalIgnoreCase))
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
        _dialogManager.Close(this);
        ShowEditor(null);
    }

    private void EditVoiceMapping(ConfiguredVoiceItem? item)
    {
        if (item is null)
            return;
        _dialogManager.Close(this);
        ShowEditor(item);
    }

    private void ShowEditor(ConfiguredVoiceItem? current)
    {
        var viewModel = new TtsEditVoiceDialogViewModel(
            _dialogManager,
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
        };
        _dialogManager.CreateDialog(viewModel).Show();
    }

    private void DeleteVoiceMapping(ConfiguredVoiceItem? item)
    {
        if (item is null)
            return;
        _settings.RemoveVoiceForLanguage(SelectedProvider, item.Language.Id);
        RefreshConfiguredVoices();
    }

    private void ReopenSettings()
    {
        var viewModel = new TtsVoiceSettingsDialogViewModel(
            _dialogManager, _toasts, _tts, _settings);
        _dialogManager.CreateDialog(viewModel).Show();
    }

    private void ShowError(string message) =>
        _toasts.CreateToast(Resources.Tts_ErrorOpeningDialog).WithContent(message).ShowError();

    private static string GetPrimaryLanguage(string languageId) =>
        languageId.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
}

public sealed class TtsEditVoiceDialogViewModel : ConventionViewModelBase
{
    private readonly DialogManager _dialogManager;
    private readonly ITtsUseCases _tts;
    private readonly string _provider;
    private readonly IReadOnlyList<TtsVoice> _allVoices;
    private TtsLanguageItem? _selectedLanguage;
    private TtsVoice? _selectedVoice;
    private string _searchText = string.Empty;
    private ObservableCollection<TtsVoice> _filteredVoices = [];

    public TtsEditVoiceDialogViewModel(
        DialogManager dialogManager,
        ITtsUseCases tts,
        string provider,
        IReadOnlyList<TtsLanguageItem> languages,
        IReadOnlyList<TtsVoice> voices,
        TtsLanguageItem? initialLanguage = null,
        string? initialVoiceId = null)
    {
        _dialogManager = dialogManager;
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
        _dialogManager.Close(this);
        if (SelectedLanguage is not null && SelectedVoice is not null)
            OnSave?.Invoke(SelectedLanguage, SelectedVoice);
    }

    private void Cancel()
    {
        _dialogManager.Close(this);
        OnCancel?.Invoke();
    }

    private void Preview()
    {
        if (SelectedVoice is null)
            return;
        _dialogManager.Close(this);
        var viewModel = new TtsPreviewInputDialogViewModel(
            _dialogManager, _tts, _provider, SelectedVoice.Id)
        {
            OnDismiss = Reopen
        };
        _dialogManager.CreateDialog(viewModel).Show();
    }

    private void Reopen()
    {
        var viewModel = new TtsEditVoiceDialogViewModel(
            _dialogManager, _tts, _provider,
            AvailableLanguages, _allVoices, SelectedLanguage, SelectedVoice?.Id)
        {
            SearchText = SearchText,
            OnSave = OnSave,
            OnCancel = OnCancel
        };
        _dialogManager.CreateDialog(viewModel).Show();
    }
}

public sealed class TtsPreviewInputDialogViewModel : ConventionViewModelBase
{
    private readonly DialogManager _dialogManager;
    private readonly ITtsUseCases _tts;
    private readonly string _provider;
    private readonly string _voiceId;
    private string _inputText = Resources.Tts_PreviewDefaultText;
    private ITtsPlaybackSession? _session;
    private CancellationTokenSource? _playbackCancellation;
    private bool _isLoading;
    private bool _isPlaying;

    public TtsPreviewInputDialogViewModel(
        DialogManager dialogManager,
        ITtsUseCases tts,
        string provider,
        string voiceId)
    {
        _dialogManager = dialogManager;
        _tts = tts;
        _provider = provider;
        _voiceId = voiceId;
        // This command must stay executable while the preview is playing so it can stop it.
        PlayCommand = ReactiveCommand.Create(() => { _ = PlayAsync(); });
        CloseCommand = ReactiveCommand.CreateFromTask(CloseAsync);
    }

    public string InputText { get => _inputText; set => this.RaiseAndSetIfChanged(ref _inputText, value); }
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isLoading, value);
            this.RaisePropertyChanged(nameof(ShowPlayIcon));
        }
    }
    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isPlaying, value);
            this.RaisePropertyChanged(nameof(ShowPlayIcon));
        }
    }
    public bool ShowPlayIcon => !IsLoading && !IsPlaying;
    public ReactiveCommand<Unit, Unit> PlayCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public Action? OnDismiss { get; init; }

    private async Task PlayAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText))
            return;

        if (IsPlaying)
        {
            await StopCurrentAsync().ConfigureAwait(false);
            return;
        }
        if (IsLoading)
        {
            _playbackCancellation?.Cancel();
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _playbackCancellation = cancellation;
        ITtsPlaybackSession? session = null;
        IsLoading = true;
        try
        {
            var result = await _tts.EnqueueWithSessionAsync(
                new TtsSynthesisRequest(InputText, _voiceId, _provider),
                interruptCurrent: true,
                cancellationToken: cancellation.Token).ConfigureAwait(false);
            if (result.IsFailure)
                return;

            session = result.Value;
            if (cancellation.IsCancellationRequested)
            {
                await session.StopAsync().ConfigureAwait(false);
                return;
            }
            _session = session;
            IsPlaying = true;
            IsLoading = false;
            await session.Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_session, session))
            {
                _session = null;
                IsPlaying = false;
            }
            IsLoading = false;
            if (ReferenceEquals(_playbackCancellation, cancellation))
                _playbackCancellation = null;
        }
    }

    private async Task StopCurrentAsync()
    {
        _playbackCancellation?.Cancel();
        var session = _session;
        if (session is null)
        {
            IsPlaying = false;
            return;
        }

        _session = null;
        IsPlaying = false;
        await session.StopAsync().ConfigureAwait(false);
    }

    private async Task CloseAsync()
    {
        await StopCurrentAsync().ConfigureAwait(false);
        _dialogManager.Close(this);
        OnDismiss?.Invoke();
    }
}
