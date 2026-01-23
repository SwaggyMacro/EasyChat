using EasyChat.ViewModels;
using Material.Icons;
using ReactiveUI;

namespace EasyChat.Models;

public abstract class Page : ViewModelBase
{
    private string _displayName;

    private MaterialIconKind _icon;

    private int _index;

    protected Page(string displayName, MaterialIconKind icon, int index = 0)
    {
        _displayName = displayName;
        _icon = icon;
        _index = index;
    }

    public string DisplayName
    {
        get => _displayName;
        set => this.RaiseAndSetIfChanged(ref _displayName, value);
    }

    public MaterialIconKind Icon
    {
        get => _icon;
        set => this.RaiseAndSetIfChanged(ref _icon, value);
    }

    public int Index
    {
        get => _index;
        set => this.RaiseAndSetIfChanged(ref _index, value);
    }
}