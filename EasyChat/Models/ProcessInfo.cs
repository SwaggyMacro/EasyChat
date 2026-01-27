using Avalonia.Media.Imaging;
using ReactiveUI;

namespace EasyChat.Models;

public class ProcessInfo : ReactiveObject
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string DisplayName => Id == 0 ? Lang.Resources.Speech_AllSystemAudio : $"[{Id}] {Name}"; 
    
    private Bitmap? _appIcon;
    public Bitmap? AppIcon
    {
        get => _appIcon;
        set => this.RaiseAndSetIfChanged(ref _appIcon, value);
    }
    
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }
}
