using EasyChat.Constants;
using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

[JsonObject(MemberSerialization.OptIn)]
public class ScreenshotConfig : ReactiveObject
{
    private string? _mode = Constant.ScreenshotMode.Precise;

    [JsonProperty]
    public string? Mode
    {
        get => _mode ?? Constant.ScreenshotMode.Precise;
        set => this.RaiseAndSetIfChanged(ref _mode, value ?? Constant.ScreenshotMode.Quick);
    }

    [JsonProperty]
    public System.Collections.ObjectModel.ObservableCollection<FixedArea> FixedAreas { get; set; } = new();
}
