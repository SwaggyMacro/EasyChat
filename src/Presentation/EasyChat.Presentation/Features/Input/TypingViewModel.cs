using System.Reactive;
using EasyChat.Contracts.Input;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Foundation.Localization;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace EasyChat.Presentation.Features.Input;

public sealed class TypingViewModel : ReactiveObject, IDisposable
{
    private readonly ExternalTargetToken _target;
    private readonly ShortcutParameterSettings? _shortcut;
    private readonly SettingsSession _settings;
    private readonly IInputTranslationUseCases _inputTranslation;
    private readonly ILogger<TypingViewModel> _logger;
    private LanguageSettings? _selectedSourceLanguage;
    private LanguageSettings? _selectedTargetLanguage;
    private bool _followGlobalLanguage;

    public TypingViewModel(
        ExternalTargetToken target,
        ShortcutParameterSettings? shortcut,
        SettingsSession settings,
        TranslationLanguageOptions languages,
        IInputTranslationUseCases inputTranslation,
        ILogger<TypingViewModel> logger)
    {
        _target = target;
        _shortcut = shortcut;
        _settings = settings;
        _inputTranslation = inputTranslation;
        _logger = logger;
        SourceLanguages = languages.All;
        TargetLanguages = languages.All;
        SwapLanguagesCommand = ReactiveCommand.Create(SwapLanguages);
        UpdateFromSettings();
        settings.Changed += OnSettingsChanged;
    }

    public ReactiveCommand<Unit, Unit> SwapLanguagesCommand { get; }

    public IReadOnlyList<LanguageSettings> SourceLanguages { get; }
    public IReadOnlyList<LanguageSettings> TargetLanguages { get; }

    public LanguageSettings? SelectedSourceLanguage
    {
        get => _selectedSourceLanguage;
        set
        {
            if (_selectedSourceLanguage == value)
                return;
            this.RaiseAndSetIfChanged(ref _selectedSourceLanguage, value);
            if (value is not null)
                _settings.Input.TypingSourceLanguage = value.Id;
        }
    }

    public LanguageSettings? SelectedTargetLanguage
    {
        get => _selectedTargetLanguage;
        set
        {
            if (_selectedTargetLanguage == value)
                return;
            this.RaiseAndSetIfChanged(ref _selectedTargetLanguage, value);
            if (value is not null)
                _settings.Input.TypingTargetLanguage = value.Id;
        }
    }

    public bool FollowGlobalLanguage
    {
        get => _followGlobalLanguage;
        set
        {
            if (_followGlobalLanguage == value)
                return;
            this.RaiseAndSetIfChanged(ref _followGlobalLanguage, value);
            _settings.Input.FollowGlobalLanguage = value;
        }
    }

    public async Task TranslateAndSendAsync(string text, CancellationToken cancellationToken = default)
    {
        var sourceId = FollowGlobalLanguage ? null : SelectedSourceLanguage?.Id;
        var targetId = FollowGlobalLanguage ? null : SelectedTargetLanguage?.Id;
        var result = await _inputTranslation.TranslateAndDeliverAsync(
            new InputTranslationRequest(
                text,
                _target,
                sourceId,
                targetId,
                _shortcut?.ReplaceCurrentInput ?? false,
                _shortcut?.InputTranslateBeforeKey,
                _shortcut?.InputTranslateAfterKey),
            cancellationToken);
        if (result.IsFailure)
            _logger.LogWarning("Input translation failed: {Error}", result.Error.Message);
    }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    private void SwapLanguages()
    {
        if (FollowGlobalLanguage)
            return;

        var source = SelectedSourceLanguage;
        SelectedSourceLanguage = SelectedTargetLanguage;
        SelectedTargetLanguage = source;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs eventArgs)
    {
        if (eventArgs.Section is SettingsSection.Input or SettingsSection.General)
            UpdateFromSettings();
    }

    private void UpdateFromSettings()
    {
        _followGlobalLanguage = _settings.Input.FollowGlobalLanguage;
        this.RaisePropertyChanged(nameof(FollowGlobalLanguage));
        var sourceId = _settings.Input.TypingSourceLanguage;
        var targetId = _settings.Input.TypingTargetLanguage;
        _selectedSourceLanguage = SourceLanguages.FirstOrDefault(language => language.Id == sourceId)
                                  ?? SourceLanguages.FirstOrDefault();
        _selectedTargetLanguage = TargetLanguages.FirstOrDefault(language => language.Id == targetId)
                                  ?? TargetLanguages.FirstOrDefault();
        this.RaisePropertyChanged(nameof(SelectedSourceLanguage));
        this.RaisePropertyChanged(nameof(SelectedTargetLanguage));
    }
}
