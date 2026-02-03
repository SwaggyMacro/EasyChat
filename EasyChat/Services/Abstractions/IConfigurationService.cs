using EasyChat.Models.Configuration;

namespace EasyChat.Services.Abstractions;

public interface IConfigurationService
{
    General? General { get; }
    AiModel? AiModel { get; }
    MachineTrans? MachineTrans { get; }
    Proxy? Proxy { get; }
    Shortcut? Shortcut { get; }
    Prompts? Prompts { get; }
    ResultConfig? Result { get; }
    InputConfig? Input { get; }
    ScreenshotConfig? Screenshot { get; }
    SelectionTranslationConfig? SelectionTranslation { get; }
    SpeechRecognitionConfig? SpeechRecognition { get; }
    TtsConfig? Tts { get; }
}