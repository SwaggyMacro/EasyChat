namespace EasyChat.Services.Languages.Providers;

using System.Collections.Generic;
using EasyChat.Services.Languages; // Ensure we see LanguageService

/// <summary>
/// A special provider for AI Models (LLMs) which prefer full English names over short codes.
/// </summary>
public class AiLanguageCodeProvider : ILanguageCodeProvider
{
    public string ProviderName => "AI";

    public string GetCode(LanguageDefinition language)
    {
        if (language == null) return "Auto Detect";

        // Prefer English name for AI reasoning
        if (!string.IsNullOrWhiteSpace(language.EnglishName))
        {
            return language.EnglishName;
        }

        // Fallback to Display Name if English Name is missing
        return language.DisplayName;
    }

    public IEnumerable<LanguageDefinition> GetSupportedLanguages()
    {
        // AI models generally support describing the language in English, so we support all registered languages.
        return LanguageService.GetAllLanguages();
    }
}
