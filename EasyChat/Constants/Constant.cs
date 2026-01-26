using System;
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

    public const string Prompt =
        "You are a very professional translator. You will translate very idiomatically and perform relevant regional optimizations (idiomatic translation) based on the content to be translated and the corresponding language." +
        "For example, in Stranger Things, \"the Upside Down\" is translated as \"The Inverted World\" in China, and \"Interstellar\" is translated as \"Interstellar Travel\" in China." +
        "In addition, you can only reply to me with the translated text and cannot include any other content.";

    // Windows Message Constants
    public const uint WM_CHAR = 0x0102;

    // Delay Constants
    public const int KeySendDelayMs = 5;

    public const int FocusSwitchDelayMs = 100;

    // Configuration path
    public static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration");

    public static class TransEngineType
    {
        public const string Machine = "MachineTrans";
        public const string Ai = "AiModel";
    }
}