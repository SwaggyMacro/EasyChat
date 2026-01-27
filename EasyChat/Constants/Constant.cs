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
    public const string SpeechRecognitionConf = "SpeechRecognition";
    
    public static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration");

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
}

[SuppressMessage("ReSharper", "InconsistentNaming")]
public static class Windows
{
    // Windows Message Constants
    public const uint WM_CHAR = 0x0102;
}