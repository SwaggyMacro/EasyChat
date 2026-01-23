using EasyChat.Services.Translation;

namespace EasyChat.Services.Abstractions;

/// <summary>
///     Factory for creating translation services based on current configuration.
/// </summary>
public interface ITranslationServiceFactory
{
    /// <summary>
    ///     Creates a translation service based on the current configuration
    ///     (uses GeneralConf.TransEngine and UsingAiModel/UsingMachineTrans).
    /// </summary>
    ITranslation CreateCurrentService();

    /// <summary>
    ///     Creates a specific AI translation service.
    /// </summary>
    /// <param name="providerName">Provider name: "OpenAI", "Gemini", or "Claude"</param>
    ITranslation CreateAiService(string providerName);

    /// <summary>
    ///     Creates a specific AI translation service by its unique ID.
    /// </summary>
    /// <param name="id">The unique ID of the AI model.</param>
    ITranslation CreateAiServiceById(string id);

    /// <summary>
    ///     Creates a specific machine translation service.
    /// </summary>
    /// <param name="providerName">Provider name: "Baidu", "Tencent", "Google", or "DeepL"</param>
    ITranslation CreateMachineService(string providerName);
}