using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using EasyChat.Models.Configuration;
using ReactiveUI;
using SukiUI.Dialogs;

namespace EasyChat.ViewModels.Dialogs;

public enum KeyListType
{
    String,
    Baidu,
    Tencent
}

public abstract class KeyItemViewModelBase : ReactiveObject
{
}

public class StringKeyItemViewModel : KeyItemViewModelBase
{
    private string _value = string.Empty;

    public string Value
    {
        get => _value;
        set => this.RaiseAndSetIfChanged(ref _value, value);
    }
}

public class BaiduKeyItemViewModel : KeyItemViewModelBase
{
    private string _appId = string.Empty;

    private string _appKey = string.Empty;

    public string AppId
    {
        get => _appId;
        set => this.RaiseAndSetIfChanged(ref _appId, value);
    }

    public string AppKey
    {
        get => _appKey;
        set => this.RaiseAndSetIfChanged(ref _appKey, value);
    }
}

public class TencentKeyItemViewModel : KeyItemViewModelBase
{
    private string _secretId = string.Empty;

    private string _secretKey = string.Empty;

    public string SecretId
    {
        get => _secretId;
        set => this.RaiseAndSetIfChanged(ref _secretId, value);
    }

    public string SecretKey
    {
        get => _secretKey;
        set => this.RaiseAndSetIfChanged(ref _secretKey, value);
    }
}

public class KeyListEditorViewModel : ViewModelBase
{
    private readonly ISukiDialog _dialog;
    private readonly KeyListType _type;

    private ObservableCollection<KeyItemViewModelBase> _items;

    public KeyListEditorViewModel(ISukiDialog dialog, string title, IEnumerable<object> existingItems, KeyListType type)
    {
        _dialog = dialog;
        Title = title;
        _type = type;

        _items = new ObservableCollection<KeyItemViewModelBase>();

        foreach (var item in existingItems)
            if (type == KeyListType.String && item is string s)
                _items.Add(new StringKeyItemViewModel { Value = s });
            else if (type == KeyListType.Baidu && item is MachineTrans.BaiduItem b)
                _items.Add(new BaiduKeyItemViewModel { AppId = b.AppId, AppKey = b.AppKey });
            else if (type == KeyListType.Tencent && item is MachineTrans.TencentItem t)
                _items.Add(new TencentKeyItemViewModel { SecretId = t.SecretId, SecretKey = t.SecretKey });

        AddCommand = ReactiveCommand.Create(ExecuteAdd);
        RemoveCommand = ReactiveCommand.Create<KeyItemViewModelBase>(ExecuteRemove);
        SaveCommand = ReactiveCommand.Create(ExecuteSave);
        CancelCommand = ReactiveCommand.Create(ExecuteCancel);
    }

    public string Title { get; }

    public ObservableCollection<KeyItemViewModelBase> Items
    {
        get => _items;
        set => this.RaiseAndSetIfChanged(ref _items, value);
    }

    public ReactiveCommand<Unit, Unit> AddCommand { get; }
    public ReactiveCommand<KeyItemViewModelBase, Unit> RemoveCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public Action<IEnumerable<object>>? OnSave { get; set; }

    private void ExecuteAdd()
    {
        switch (_type)
        {
            case KeyListType.String:
                Items.Add(new StringKeyItemViewModel());
                break;
            case KeyListType.Baidu:
                Items.Add(new BaiduKeyItemViewModel());
                break;
            case KeyListType.Tencent:
                Items.Add(new TencentKeyItemViewModel());
                break;
        }
    }

    private void ExecuteRemove(KeyItemViewModelBase item)
    {
        Items.Remove(item);
    }

    private void ExecuteSave()
    {
        var result = new List<object>();
        foreach (var item in Items)
            if (item is StringKeyItemViewModel s && !string.IsNullOrWhiteSpace(s.Value))
                result.Add(s.Value);
            else if (item is BaiduKeyItemViewModel b)
                result.Add(new MachineTrans.BaiduItem { AppId = b.AppId, AppKey = b.AppKey });
            else if (item is TencentKeyItemViewModel t)
                result.Add(new MachineTrans.TencentItem { SecretId = t.SecretId, SecretKey = t.SecretKey });

        OnSave?.Invoke(result);
        _dialog.Dismiss();
    }

    private void ExecuteCancel()
    {
        _dialog.Dismiss();
    }
}