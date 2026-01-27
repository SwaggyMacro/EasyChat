using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

[JsonObject(MemberSerialization.OptIn)]
public class SpeechRecognitionConfig : ReactiveObject
{
    [JsonProperty]
    public string RecognitionLanguage
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "";

    [JsonProperty]
    public bool IsTranslationEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    [JsonProperty]
    public bool IsRealTimePreviewEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    [JsonProperty]
    public string TargetLanguage
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "";

    [JsonProperty]
    public string EngineId
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "";

    // 0 = Machine, 1 = AI. 
    // Using int or string enum for serialization safety.
    [JsonProperty]
    public int EngineType
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    [JsonProperty]
    public int MaxSentencesPerLine
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 1;

    // 0 = Segmented (Default), 1 = Auto Scroll
    [JsonProperty]
    public FloatingDisplayMode FloatingDisplayMode
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = FloatingDisplayMode.Segmented;

    [JsonProperty]
    public int MaxFloatingHistory
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 2;

    // --- Floating Subtitle Configuration ---

    // Primary Subtitle (Main)
    
    // Default = Original
    [JsonProperty]
    public SubtitleSource MainSubtitleSource
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = SubtitleSource.Original;

    [JsonProperty("FontSize")] // Backward compatibility
    public double PrimaryFontSize
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 20.0;

    [JsonProperty("FontFamily")]
    public string PrimaryFontFamily
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "Microsoft YaHei UI";

    [JsonProperty("FontColor")]
    public string PrimaryFontColor
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "#FFFFFFFF";

    // Secondary Subtitle (Auxiliary)

    // Default = Translated
    [JsonProperty]
    public SubtitleSource SecondarySubtitleSource
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = SubtitleSource.Translated;

    [JsonProperty]
    public double SecondaryFontSize
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 16.0;

    [JsonProperty]
    public string SecondaryFontFamily
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "Microsoft YaHei UI";

    [JsonProperty]
    public string SecondaryFontColor
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "#FFCCCCCC";

    [JsonProperty]
    public string BackgroundColor
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "#99000000";

    [JsonProperty]
    public string SubtitleBackgroundColor
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "#00000000";

    [JsonProperty]
    public double WindowOpacity
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = 0.8;

    [JsonProperty]
    public bool IsFloatingWindowLocked
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    [JsonProperty]
    public string FloatingWindowOrientation
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "Horizontal";

    // Window Position and Size
    [JsonProperty]
    public double WindowX
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = -1;

    [JsonProperty]
    public double WindowY
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = -1;

    [JsonProperty]
    public double WindowWidth
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = -1;

    [JsonProperty]
    public double WindowHeight
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = -1;
}

public enum FloatingDisplayMode
{
    Segmented = 0,
    AutoScroll = 1
}

public enum SubtitleSource
{
    None = 0,
    Original = 1,
    Translated = 2
}
