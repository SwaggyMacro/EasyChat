using EasyChat.Services.Abstractions;

namespace EasyChat.Services.Languages.Providers;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Abstract base class for language code providers.
/// </summary>
public abstract class BaseLanguageCodeProvider : ILanguageCodeProvider
{
    public abstract string ProviderName { get; }

    public virtual string GetCode(LanguageDefinition language)
    {
        if (language == null) return "auto"; // Default fallback
        
        var code = language.GetCode(ProviderName);
        if (!string.IsNullOrEmpty(code))
        {
            return code;
        }

        // Fallback Logic:
        // 1. If provider is "Baidu" or "Tencent" and the language is English, default to "en".
        // 2. Return Id as a safe bet if nothing is found (often standard ISO codes work).
        
        return language.Id;
    }

    public virtual IEnumerable<LanguageDefinition> GetSupportedLanguages()
    {
        // Return languages where we have an explicit code mapping for this provider
        return LanguageService.GetAllLanguages()
            .Where(x => !string.IsNullOrEmpty(x.GetCode(ProviderName)));
    }
}
