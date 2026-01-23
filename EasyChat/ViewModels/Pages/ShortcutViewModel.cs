using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Controls.Notifications;
using EasyChat.Lang;
using EasyChat.Models;
using EasyChat.Models.Configuration;
using EasyChat.Services.Abstractions;
using EasyChat.ViewModels.Dialogs;
using Material.Icons;
using ReactiveUI;
using SukiUI.Dialogs;

namespace EasyChat.ViewModels.Pages;

public class ShortcutViewModel : Page
{
    private readonly IConfigurationService _configurationService;
    private readonly ISukiDialogManager _dialogManager;

    // Basic shortcuts (Screenshot, etc.)
    private ObservableCollection<ShortcutEntry> _basicShortcuts = new();

    // Language switch shortcuts
    private ObservableCollection<ShortcutEntry> _languageShortcuts = new();

    public ShortcutViewModel(ISukiDialogManager dialogManager, IConfigurationService configurationService) : base(
        Resources.Shortcut, MaterialIconKind.Keyboard, 2)
    {
        _dialogManager = dialogManager;
        _configurationService = configurationService;

        LoadShortcutsFromService();

        // Subscribe to external changes (optional but good for consistency)
        // If entries are added externally or by other means, refresh local view
        Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => _configurationService.Shortcut.Entries.CollectionChanged += h,
                h => _configurationService.Shortcut.Entries.CollectionChanged -= h)
            .Subscribe(_ => LoadShortcutsFromService());

        AddEntryCommand = ReactiveCommand.Create<string>(AddEntry);
        EditEntryCommand = ReactiveCommand.Create<ShortcutEntry>(EditEntry);
        RemoveEntryCommand = ReactiveCommand.Create<ShortcutEntry>(RemoveEntry);
    }

    public ObservableCollection<ShortcutEntry> BasicShortcuts
    {
        get => _basicShortcuts;
        set => this.RaiseAndSetIfChanged(ref _basicShortcuts, value);
    }

    public ObservableCollection<ShortcutEntry> LanguageShortcuts
    {
        get => _languageShortcuts;
        set => this.RaiseAndSetIfChanged(ref _languageShortcuts, value);
    }

    // public ReactiveCommand<Unit, Unit> SaveCommand { get; } // Removed
    // public ReactiveCommand<Unit, Unit> RestoreCommand { get; } // Removed

    public ReactiveCommand<string, Unit> AddEntryCommand { get; }
    public ReactiveCommand<ShortcutEntry, Unit> EditEntryCommand { get; }
    public ReactiveCommand<ShortcutEntry, Unit> RemoveEntryCommand { get; }

    private readonly string[] _basicTypes = { "Screenshot", "InputTranslate" };
    private readonly string[] _languageTypes = { "SwitchEngineSourceTarget" };

    private void LoadShortcutsFromService()
    {
        var entries = _configurationService.Shortcut.Entries;

        BasicShortcuts = new ObservableCollection<ShortcutEntry>(
            entries.Where(e => _basicTypes.Contains(e.ActionType)));

        LanguageShortcuts = new ObservableCollection<ShortcutEntry>(
            entries.Where(e => _languageTypes.Contains(e.ActionType)));
    }

    // AttachAutoSave and OnItemPropertyChanged removed (handled by service)

    // Save method removed

    private void AddEntry(string category)
    {
        // Check if there are pre-defined actions for this category to select a default
        ShortcutEntry? template = null;
        string[] allowedTypes;

        if (category == "Basic")
        {
            template = new ShortcutEntry { ActionType = "Screenshot" };
            allowedTypes = _basicTypes;
        }
        else
        {
            template = new ShortcutEntry { ActionType = "SwitchEngineSourceTarget" };
            allowedTypes = _languageTypes;
        }

        ShowEditDialog(template, true, allowedTypes);
    }

    private void EditEntry(ShortcutEntry entry)
    {
        string[] allowedTypes;
        if (_basicTypes.Contains(entry.ActionType))
        {
            allowedTypes = _basicTypes;
        }
        else
        {
            allowedTypes = _languageTypes;
        }
        ShowEditDialog(entry, false, allowedTypes);
    }

    private void RemoveEntry(ShortcutEntry entry)
    {
        _dialogManager.CreateDialog()
            .OfType(NotificationType.Warning)
            .WithTitle(Resources.ConfirmDeletion)
            .WithContent(Resources.AreYouSureDelete)
            .WithActionButton(Resources.Delete, _ =>
            {
                // Remove from service collection - this triggers auto-save
                _configurationService.Shortcut.Entries.Remove(entry);
                // Local collections will update via LoadShortcutsFromService subscription
            }, true)
            .WithActionButton(Resources.Cancel, _ => { }, true)
            .TryShow();
    }

    private void ShowEditDialog(ShortcutEntry? entry, bool isNew, string[] allowedTypes)
    {
        _dialogManager.CreateDialog()
            .WithViewModel(dialog =>
            {
                var vm = new ShortcutEditDialogViewModel(dialog, _configurationService, allowedTypes, isNew ? null : entry);

                // If it's a new entry but we want to hint the type (passed as entry for template)
                if (isNew && entry != null)
                {
                    var def = ShortcutActionDefinition.GetByType(entry.ActionType);
                    if (def != null) vm.SelectedAction = def;
                }

                vm.OnClose = result =>
                {
                    if (result != null)
                    {
                        if (isNew)
                        {
                            AddEntryToCollection(result);
                        }
                        else
                        {
                            _configurationService.Shortcut.Entries.Remove(entry!);
                            _configurationService.Shortcut.Entries.Add(result);
                        }
                    }
                };
                return vm;
            })
            .TryShow();
    }

    private void AddEntryToCollection(ShortcutEntry entry)
    {
        // Add to service
        _configurationService.Shortcut.Entries.Add(entry);
        // Local collections update via subscription
    }
}