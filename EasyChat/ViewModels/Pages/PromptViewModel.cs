using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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

public class PromptViewModel : Page
{
    private readonly IConfigurationService _configurationService;
    private readonly ISukiDialogManager _dialogManager;

    private ObservableCollection<PromptEntry> _prompts = new();

    public PromptViewModel(ISukiDialogManager dialogManager, IConfigurationService configurationService)
        : base(Resources.Prompts, MaterialIconKind.TextBox, 3)
    {
        _dialogManager = dialogManager;
        _configurationService = configurationService;

        // Load prompts from service
        LoadPromptsFromService();

        // Subscribe to collection changes
        Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => _configurationService.Prompts?.Entries.CollectionChanged += h,
                h => _configurationService.Prompts?.Entries.CollectionChanged -= h)
            .Subscribe(_ => LoadPromptsFromService());

        // Commands
        AddPromptCommand = ReactiveCommand.Create(AddPrompt);
        EditPromptCommand = ReactiveCommand.Create<PromptEntry>(EditPrompt);
        RemovePromptCommand = ReactiveCommand.Create<PromptEntry>(RemovePrompt);
        SetDefaultCommand = ReactiveCommand.Create<PromptEntry>(SetDefault);
    }

    public ObservableCollection<PromptEntry> Prompts
    {
        get => _prompts;
        set => this.RaiseAndSetIfChanged(ref _prompts, value);
    }

    public ReactiveCommand<Unit, Unit> AddPromptCommand { get; }
    public ReactiveCommand<PromptEntry, Unit> EditPromptCommand { get; }
    public ReactiveCommand<PromptEntry, Unit> RemovePromptCommand { get; }
    public ReactiveCommand<PromptEntry, Unit> SetDefaultCommand { get; }

    private void LoadPromptsFromService()
    {
        if (_configurationService.Prompts != null)
            Prompts = new ObservableCollection<PromptEntry>(_configurationService.Prompts.Entries);
    }

    private void AddPrompt()
    {
        ShowEditDialog(null, true);
    }

    private void EditPrompt(PromptEntry entry)
    {
        ShowEditDialog(entry, false);
    }

    private void RemovePrompt(PromptEntry entry)
    {
        // Prevent deleting the last prompt or the default prompt
        if (_configurationService.Prompts?.Entries.Count <= 1)
        {
            _dialogManager.CreateDialog()
                .OfType(NotificationType.Warning)
                .WithTitle(Resources.Delete)
                .WithContent(Resources.CannotDeleteLastPrompt)
                .Dismiss().ByClickingBackground()
                .TryShow();
            return;
        }

        if (entry.IsDefault)
        {
            _dialogManager.CreateDialog()
                .OfType(NotificationType.Warning)
                .WithTitle(Resources.Delete)
                .WithContent(Resources.CannotDeleteDefaultPrompt)
                .Dismiss().ByClickingBackground()
                .TryShow();
            return;
        }

        _dialogManager.CreateDialog()
            .OfType(NotificationType.Warning)
            .WithTitle(Resources.ConfirmDeletion)
            .WithContent(Resources.ConfirmDeletePrompt)
            .WithActionButton(Resources.Delete, _ =>
            {
                _configurationService.Prompts?.Entries.Remove(entry);
            }, true)
            .WithActionButton(Resources.Cancel, _ => { }, true)
            .TryShow();
    }

    private void SetDefault(PromptEntry entry)
    {
        // Clear all other defaults
        if (_configurationService.Prompts == null) return;
        foreach (var prompt in _configurationService.Prompts.Entries)
        {
            prompt.IsDefault = false;
        }

        entry.IsDefault = true;
        _configurationService.Prompts?.SelectedPromptId = entry.Id;
    }

    private void ShowEditDialog(PromptEntry? entry, bool isNew)
    {
        _dialogManager.CreateDialog()
            .WithViewModel(dialog =>
            {
                var vm = new PromptEditDialogViewModel(dialog, isNew ? null : entry)
                {
                    OnClose = result =>
                    {
                        if (result != null)
                        {
                            if (isNew)
                            {
                                _configurationService.Prompts?.Entries.Add(result);
                            }
                            else if (entry != null)
                            {
                                // Update existing entry
                                entry.Name = result.Name;
                                entry.Content = result.Content;
                            }
                        }
                    }
                };

                return vm;
            })
            .TryShow();
    }
}
