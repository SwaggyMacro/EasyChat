using System;
using Avalonia;
using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

[JsonObject(MemberSerialization.OptIn)]
public class FixedArea : ReactiveObject
{
    private string _name = string.Empty;
    private int _x;
    private int _y;
    private int _width;
    private int _height;
    private bool _isEnabled = true;

    [JsonProperty]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonProperty]
    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    [JsonProperty]
    public int X
    {
        get => _x;
        set => this.RaiseAndSetIfChanged(ref _x, value);
    }

    [JsonProperty]
    public int Y
    {
        get => _y;
        set => this.RaiseAndSetIfChanged(ref _y, value);
    }

    [JsonProperty]
    public int Width
    {
        get => _width;
        set => this.RaiseAndSetIfChanged(ref _width, value);
    }

    [JsonProperty]
    public int Height
    {
        get => _height;
        set => this.RaiseAndSetIfChanged(ref _height, value);
    }

    [JsonProperty]
    public bool IsEnabled
    {
        get => _isEnabled;
        set => this.RaiseAndSetIfChanged(ref _isEnabled, value);
    }

    public PixelRect ToPixelRect() => new(X, Y, Width, Height);

    public string DisplayInfo => $"X:{X}, Y:{Y}, W:{Width}, H:{Height}";
}
