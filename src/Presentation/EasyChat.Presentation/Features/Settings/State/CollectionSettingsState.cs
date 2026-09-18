using System.Collections.ObjectModel;
using EasyChat.Contracts.Settings;
using EasyChat.Presentation.Features.Shortcuts;
using ReactiveUI;

namespace EasyChat.Presentation.Features.Settings.State;

public sealed class PromptEntryState : LiveSettingsSection
{
    private string _name;
    private string _role;
    private bool _isDefault;

    public PromptEntryState(PromptEntrySettings value, Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.Prompts, commit)
    {
        Id = value.Id;
        _name = value.Name;
        _role = value.Content;
        _isDefault = value.IsDefault;
    }

    public string Id { get; }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Role
    {
        get => _role;
        set
        {
            if (Set(ref _role, value))
            {
                this.RaisePropertyChanged(nameof(RolePreview));
                this.RaisePropertyChanged(nameof(ContentPreview));
            }
        }
    }
    public bool IsDefault
    {
        get => _isDefault;
        set => SetDefaultState(value, commit: true);
    }

    internal bool SetDefaultState(bool value, bool commit)
    {
        if (_isDefault == value)
            return false;

        this.RaiseAndSetIfChanged(ref _isDefault, value, nameof(IsDefault));
        if (commit)
            Commit();
        return true;
    }
    public string RolePreview => Role.Length > 100 ? Role[..100] + "..." : Role;
    public string ContentPreview => RolePreview;
    public PromptEntrySettings ToContract() => new(Id, Name, Role, IsDefault);
}

public sealed class LivePromptSettings : LiveSettingsSection
{
    private string _selectedPromptId;

    public LivePromptSettings(PromptSettings value, Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.Prompts, commit)
    {
        _selectedPromptId = value.SelectedPromptId;
        Entries = new ObservableCollection<PromptEntryState>(
            value.Entries.Select(entry => new PromptEntryState(entry, commit)));
        SynchronizeDefaultPrompt();
        Entries.CollectionChanged += OnEntriesChanged;
    }

    public string SelectedPromptId
    {
        get => _selectedPromptId;
        set
        {
            if (_selectedPromptId == value)
                return;

            this.RaiseAndSetIfChanged(ref _selectedPromptId, value, nameof(SelectedPromptId));
            SynchronizeDefaultPrompt();
            Commit();
        }
    }

    public ObservableCollection<PromptEntryState> Entries { get; }
    public PromptSettings ToContract() => new(SelectedPromptId, Entries.Select(entry => entry.ToContract()).ToArray());

    private void OnEntriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        SynchronizeDefaultPrompt();
        Commit();
    }

    private void SynchronizeDefaultPrompt()
    {
        var selected = Entries.FirstOrDefault(entry =>
            string.Equals(entry.Id, _selectedPromptId, StringComparison.Ordinal));
        if (selected is null)
            return;

        foreach (var entry in Entries)
            entry.SetDefaultState(ReferenceEquals(entry, selected), commit: false);
    }
}

public sealed class ShortcutParameterState
{
    public ShortcutParameterState(ShortcutParameterSettings value)
    {
        Engine = value.Engine;
        EngineId = value.EngineId;
        Source = value.Source;
        Target = value.Target;
        Value = value.Value;
        ReadSelectedText = value.ReadSelectedText;
        InputTranslateBeforeKey = value.InputTranslateBeforeKey;
        InputTranslateAfterKey = value.InputTranslateAfterKey;
        ReplaceCurrentInput = value.ReplaceCurrentInput;
        TextAssistMode = value.TextAssistMode;
        ShowSelectionToolbar = value.ShowSelectionToolbar;
        AutomaticTrigger = value.AutomaticTrigger;
        AutomaticPressMode = value.AutomaticPressMode;
    }

    public string Engine { get; set; }
    public string? EngineId { get; set; }
    public LanguageSettings? Source { get; set; }
    public LanguageSettings? Target { get; set; }
    public string? Value { get; set; }
    public bool? ReadSelectedText { get; set; }
    public string? InputTranslateBeforeKey { get; set; }
    public string? InputTranslateAfterKey { get; set; }
    public bool? ReplaceCurrentInput { get; set; }
    public TextAssistShortcutMode? TextAssistMode { get; set; }
    public bool? ShowSelectionToolbar { get; set; }
    public AutomaticShortcutTrigger? AutomaticTrigger { get; set; }
    public AutomaticShortcutPressMode? AutomaticPressMode { get; set; }

    public ShortcutParameterSettings ToContract() => new(
        Engine, EngineId, Source, Target, Value, ReadSelectedText,
        InputTranslateBeforeKey, InputTranslateAfterKey, ReplaceCurrentInput,
        TextAssistMode, ShowSelectionToolbar, AutomaticTrigger, AutomaticPressMode);
}

public sealed class ShortcutEntryState : LiveSettingsSection
{
    private string _actionType;
    private ShortcutParameterState? _parameter;
    private string _keyCombination;
    private bool _isEnabled;
    private string? _remark;

    public ShortcutEntryState(
        ShortcutEntrySettings value,
        Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.Shortcut, commit)
    {
        _actionType = value.ActionType;
        _parameter = value.Parameter is null ? null : new ShortcutParameterState(value.Parameter);
        _keyCombination = value.KeyCombination;
        _isEnabled = value.IsEnabled;
        _remark = value.Remark;
    }

    public string ActionType
    {
        get => _actionType;
        set
        {
            if (Set(ref _actionType, value))
            {
                this.RaisePropertyChanged(nameof(ActionDisplayText));
                this.RaisePropertyChanged(nameof(DisplayTitle));
            }
        }
    }
    public ShortcutParameterState? Parameter
    {
        get => _parameter;
        set
        {
            if (Set(ref _parameter, value))
                this.RaisePropertyChanged(nameof(ParameterDisplayText));
        }
    }
    public string KeyCombination { get => _keyCombination; set => Set(ref _keyCombination, value); }
    public bool IsEnabled { get => _isEnabled; set => Set(ref _isEnabled, value); }
    public string? Remark
    {
        get => _remark;
        set
        {
            if (Set(ref _remark, value))
            {
                this.RaisePropertyChanged(nameof(DisplayTitle));
                this.RaisePropertyChanged(nameof(HasRemark));
            }
        }
    }
    public string ActionDisplayText => ShortcutActionCatalog.GetDisplayName(ActionType);
    public string DisplayTitle => HasRemark ? Remark!.Trim() : ActionDisplayText;
    public bool HasRemark => !string.IsNullOrWhiteSpace(Remark);
    public string ParameterDisplayText => Parameter?.Value
        ?? (Parameter is null ? string.Empty : FormatParameter(Parameter));

    public ShortcutEntrySettings ToContract() => new(
        ActionType,
        Parameter?.ToContract(),
        KeyCombination,
        IsEnabled,
        HasRemark ? Remark!.Trim() : null);

    private static string FormatParameter(ShortcutParameterState parameter)
    {
        var display = parameter.Engine;
        if (parameter.Source is not null && parameter.Target is not null)
            display += $" ({parameter.Source.DisplayName} -> {parameter.Target.DisplayName})";
        return display;
    }
}

public sealed class LiveShortcutSettings : LiveSettingsSection
{
    public LiveShortcutSettings(ShortcutSettings value, Func<SettingsSection, EasyChat.Shared.Results.Result> commit)
        : base(SettingsSection.Shortcut, commit)
    {
        Entries = new ObservableCollection<ShortcutEntryState>(
            value.Entries.Select(entry => new ShortcutEntryState(entry, commit)));
        Entries.CollectionChanged += (_, _) => Commit();
    }

    public ObservableCollection<ShortcutEntryState> Entries { get; }
    public ShortcutSettings ToContract() => new(Entries.Select(entry => entry.ToContract()).ToArray());
}
