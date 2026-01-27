using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

[JsonObject(MemberSerialization.OptIn)]
public class SpeechRecognitionConfig : ReactiveObject
{
    private string _recognitionLanguage = "";

    [JsonProperty]
    public string RecognitionLanguage
    {
        get => _recognitionLanguage;
        set => this.RaiseAndSetIfChanged(ref _recognitionLanguage, value);
    }

    private bool _isTranslationEnabled;

    [JsonProperty]
    public bool IsTranslationEnabled
    {
        get => _isTranslationEnabled;
        set => this.RaiseAndSetIfChanged(ref _isTranslationEnabled, value);
    }

    private bool _isRealTimePreviewEnabled;

    [JsonProperty]
    public bool IsRealTimePreviewEnabled
    {
        get => _isRealTimePreviewEnabled;
        set => this.RaiseAndSetIfChanged(ref _isRealTimePreviewEnabled, value);
    }

    private string _targetLanguage = "";

    [JsonProperty]
    public string TargetLanguage
    {
        get => _targetLanguage;
        set => this.RaiseAndSetIfChanged(ref _targetLanguage, value);
    }

    private string _engineId = "";

    [JsonProperty]
    public string EngineId
    {
        get => _engineId;
        set => this.RaiseAndSetIfChanged(ref _engineId, value);
    }
    
    private int _engineType; 
    // 0 = Machine, 1 = AI. 
    // Using int or string enum for serialization safety.
    [JsonProperty]
    public int EngineType
    {
        get => _engineType;
        set => this.RaiseAndSetIfChanged(ref _engineType, value);
    }

    // --- Floating Subtitle Configuration ---

    // Primary Subtitle (Main)
    
    private int _mainSubtitleSource = 0; // 0 = Original, 1 = Translated
    [JsonProperty]
    public int MainSubtitleSource
    {
        get => _mainSubtitleSource;
        set => this.RaiseAndSetIfChanged(ref _mainSubtitleSource, value);
    }

    private double _primaryFontSize = 20.0;
    [JsonProperty("FontSize")] // Backward compatibility
    public double PrimaryFontSize
    {
        get => _primaryFontSize;
        set => this.RaiseAndSetIfChanged(ref _primaryFontSize, value);
    }

    private string _primaryFontFamily = "Microsoft YaHei UI";
    [JsonProperty("FontFamily")]
    public string PrimaryFontFamily
    {
        get => _primaryFontFamily;
        set => this.RaiseAndSetIfChanged(ref _primaryFontFamily, value);
    }

    private string _primaryFontColor = "#FFFFFFFF"; // White
    [JsonProperty("FontColor")]
    public string PrimaryFontColor
    {
        get => _primaryFontColor;
        set => this.RaiseAndSetIfChanged(ref _primaryFontColor, value);
    }

    // Secondary Subtitle (Auxiliary)

    private int _secondarySubtitleSource = 2; // 0 = None, 1 = Original, 2 = Translated. (Default Translated)
    // Actually, simplifying: 0 = None, 1 = Translated, 2 = Original ?? 
    // ViewModel says: 0=None, 1=Original, 2=Translated.
    // So 2 is Translated.
    [JsonProperty]
    public int SecondarySubtitleSource
    {
        get => _secondarySubtitleSource;
        set => this.RaiseAndSetIfChanged(ref _secondarySubtitleSource, value);
    }

    private double _secondaryFontSize = 16.0;
    [JsonProperty]
    public double SecondaryFontSize
    {
        get => _secondaryFontSize;
        set => this.RaiseAndSetIfChanged(ref _secondaryFontSize, value);
    }

    private string _secondaryFontFamily = "Microsoft YaHei UI";
    [JsonProperty]
    public string SecondaryFontFamily
    {
        get => _secondaryFontFamily;
        set => this.RaiseAndSetIfChanged(ref _secondaryFontFamily, value);
    }

    private string _secondaryFontColor = "#FFCCCCCC"; // Light Gray
    [JsonProperty]
    public string SecondaryFontColor
    {
        get => _secondaryFontColor;
        set => this.RaiseAndSetIfChanged(ref _secondaryFontColor, value);
    }

    private string _backgroundColor = "#99000000"; // Semi-transparent black
    [JsonProperty]
    public string BackgroundColor
    {
        get => _backgroundColor;
        set => this.RaiseAndSetIfChanged(ref _backgroundColor, value);
    }
    
    private string _subtitleBackgroundColor = "#00000000"; // Transparent default
    [JsonProperty]
    public string SubtitleBackgroundColor
    {
        get => _subtitleBackgroundColor;
        set => this.RaiseAndSetIfChanged(ref _subtitleBackgroundColor, value);
    }

    private double _windowOpacity = 0.8;
    [JsonProperty]
    public double WindowOpacity
    {
        get => _windowOpacity;
        set => this.RaiseAndSetIfChanged(ref _windowOpacity, value);
    }

    private bool _isFloatingWindowLocked;
    [JsonProperty]
    public bool IsFloatingWindowLocked
    {
        get => _isFloatingWindowLocked;
        set => this.RaiseAndSetIfChanged(ref _isFloatingWindowLocked, value);
    }

    private string _floatingWindowOrientation = "Horizontal";
    [JsonProperty]
    public string FloatingWindowOrientation
    {
        get => _floatingWindowOrientation;
        set => this.RaiseAndSetIfChanged(ref _floatingWindowOrientation, value);
    }
    
    // Window Position and Size
    private double _windowX = -1;
    [JsonProperty]
    public double WindowX
    {
        get => _windowX;
        set => this.RaiseAndSetIfChanged(ref _windowX, value);
    }

    private double _windowY = -1;
    [JsonProperty]
    public double WindowY
    {
        get => _windowY;
        set => this.RaiseAndSetIfChanged(ref _windowY, value);
    }

    private double _windowWidth = -1;
    [JsonProperty]
    public double WindowWidth
    {
        get => _windowWidth;
        set => this.RaiseAndSetIfChanged(ref _windowWidth, value);
    }

    private double _windowHeight = -1;
    [JsonProperty]
    public double WindowHeight
    {
        get => _windowHeight;
        set => this.RaiseAndSetIfChanged(ref _windowHeight, value);
    }
}
