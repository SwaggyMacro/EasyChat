using Material.Icons;
using ReactiveUI;

namespace EasyChat.Presentation.Foundation.Navigation;

public abstract class ViewModelBase : ReactiveObject;

public abstract class ConventionViewModelBase : ViewModelBase;

public abstract class NavigationPageViewModel(
    string displayName,
    MaterialIconKind icon,
    int index = 0) : ConventionViewModelBase
{
    private string _displayName = displayName;
    private MaterialIconKind _icon = icon;
    private int _index = index;
    private bool _showAttentionBadge;
    private string? _attentionBadgeText;

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

    /// <summary>Optional sidebar attention dot for incomplete setup.</summary>
    public bool ShowAttentionBadge
    {
        get => _showAttentionBadge;
        set => this.RaiseAndSetIfChanged(ref _showAttentionBadge, value);
    }

    public string? AttentionBadgeText
    {
        get => _attentionBadgeText;
        set => this.RaiseAndSetIfChanged(ref _attentionBadgeText, value);
    }
}
