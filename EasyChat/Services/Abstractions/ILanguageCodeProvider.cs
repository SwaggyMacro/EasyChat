using System.Collections.Generic;
using EasyChat.Services.Languages;

namespace EasyChat.Services.Abstractions;

/// <summary>
/// Interface for a service that provides language codes for a specific translation API.
/// </summary>
public interface ILanguageCodeProvider
{
    /// <summary>
    /// The unique name of the provider (e.g., "Baidu", "Google").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Gets the API-specific short code for the given language.
    /// Returns the provider-specific code if mapped, otherwise may return a fallback or the internal ID.
    /// </summary>
    /// <param name="language">The language definition.</param>
    /// <returns>The code string required by the API.</returns>
    /// <summary>
    /// Gets the list of languages supported by this provider.
    /// </summary>
    /// <returns>A collection of supported language definitions.</returns>
    IEnumerable<LanguageDefinition> GetSupportedLanguages();

    string GetCode(LanguageDefinition language);
}
