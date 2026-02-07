using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace EasyChat.Constants;

public static class Constant
{
    public const string General = "General";
    public const string AiModelConf = "AiModel";
    public const string MachineTransConf = "MachineTrans";
    public const string ProxyConf = "Proxy";
    public const string ShortcutConf = "Shortcut";
    public const string PromptConf = "Prompts";
    public const string ResultConf = "Result";
    public const string InputConf = "Input";
    public const string ScreenshotConf = "Screenshot";
    public const string SpeechRecognitionConf = "SpeechRecognition";
    public const string SelectionTranslationConf = "SelectionTranslation";
    public const string TtsConf = "Tts";
    
#if DEBUG
    public static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration");
#else
    public static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Configuration");
#endif

    public static class TransEngineType
    {
        public const string Machine = "MachineTrans";
        public const string Ai = "AiModel";
    }
    
    public static class MachineTranslationProviders
    {
        public const string Google = "Google";
        public const string DeepL = "DeepL";
        public const string Baidu = "Baidu";
        public const string Tencent = "Tencent";
    }


    public static class SelectionTranslationProviderType
    {
        public const string AiModel = "AiModel";
        public const string Machine = "MachineTrans";
    }


    public static class ScreenshotMode
    {
        public const string Precise = "Precise";
        public const string Quick = "Quick";
    }
}

[SuppressMessage("ReSharper", "InconsistentNaming")]
public static class Windows
{
    // Windows Message Constants
    public const uint WM_CHAR = 0x0102;
}