using System;
using System.Reactive;
using EasyChat.Lang;
using EasyChat.Models.Configuration;
using ReactiveUI;
using SukiUI.Dialogs;

namespace EasyChat.ViewModels.Dialogs;

public class PromptEditDialogViewModel : ViewModelBase
{
    private readonly ISukiDialog _dialog;
    private readonly PromptEntry? _originalEntry;

    private string _name = string.Empty;
    private string _content = string.Empty;

    public PromptEditDialogViewModel(ISukiDialog dialog, PromptEntry? existingEntry = null)
    {
        _dialog = dialog;
        _originalEntry = existingEntry;

        // If editing, copy values
        if (_originalEntry != null)
        {
            _name = _originalEntry.Name;
            _content = _originalEntry.Content;
        }

        var canSave = this.WhenAnyValue(
            x => x.Name,
            x => x.Content,
            (name, content) => !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(content));

        SaveCommand = ReactiveCommand.Create(Save, canSave);
        CancelCommand = ReactiveCommand.Create(Cancel);
    }

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public string Content
    {
        get => _content;
        set => this.RaiseAndSetIfChanged(ref _content, value);
    }

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public Action<PromptEntry?>? OnClose { get; set; }

    private void Save()
    {
        var result = new PromptEntry
        {
            Id = _originalEntry?.Id ?? Guid.NewGuid().ToString(),
            Name = Name,
            Content = Content,
            IsDefault = _originalEntry?.IsDefault ?? false
        };

        OnClose?.Invoke(result);
        _dialog.Dismiss();
    }

    private void Cancel()
    {
        OnClose?.Invoke(null);
        _dialog.Dismiss();
    }
}
