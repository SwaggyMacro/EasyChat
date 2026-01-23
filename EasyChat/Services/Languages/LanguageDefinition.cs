using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Newtonsoft.Json;

namespace EasyChat.Services.Languages;

/// <summary>
/// Represents a specific language with supported codes for various providers.
/// </summary>
public class LanguageDefinition
{
    /// <summary>
    /// Internal Unique Identifier for the language (e.g., "en", "zh-CN").
    /// </summary>
    [JsonProperty]
    public string Id { get; private set; }

    /// <summary>
    /// Chinese Name of the language (e.g., "简体中文", "英语").
    /// </summary>
    [JsonProperty]
    public string ChineseName { get; private set; }

    /// <summary>
    /// English Name used for AI prompts and English UI (e.g., "Simplified Chinese", "English").
    /// </summary>
    [JsonProperty]
    public string EnglishName { get; private set; }

    /// <summary>
    /// Filename of the flag icon (e.g., "cn.png").
    /// </summary>
    [JsonProperty]
    public string Icon { get; private set; }

    /// <summary>
    /// Returns the appropriate localized name based on current UI culture.
    /// Returns ChineseName if culture is Chinese, otherwise EnglishName.
    /// </summary>
    public string LocalizedName
    {
        get
        {
            var culture = Thread.CurrentThread.CurrentUICulture;
            return culture.TwoLetterISOLanguageName == "zh" ? ChineseName : EnglishName;
        }
    }

    /// <summary>
    /// Alias for LocalizedName for backward compatibility and XAML binding.
    /// </summary>
    public string DisplayName => LocalizedName;

    /// <summary>
    /// Dictionary of Provider Name -> Short Code.
    /// </summary>
    [JsonProperty]
    public Dictionary<string, string> ProviderCodes { get; private set; } = new();

    /// <summary>
    /// Private constructor for JSON deserialization.
    /// </summary>
    [JsonConstructor]
    private LanguageDefinition()
    {
        Id = string.Empty;
        ChineseName = string.Empty;
        EnglishName = string.Empty;
        Icon = string.Empty;
    }

    public LanguageDefinition(string id, string chineseName, string englishName, string icon)
    {
        Id = id;
        ChineseName = chineseName;
        EnglishName = englishName;
        Icon = icon;
    }

    /// <summary>
    /// Registers a specific code for a provider.
    /// </summary>
    /// <param name="providerName">The provider name (e.g., "Baidu").</param>
    /// <param name="code">The short code (e.g., "zh").</param>
    /// <returns>Is self for fluent chaining.</returns>
    public LanguageDefinition WithCode(string providerName, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return this;
        ProviderCodes[providerName] = code;
        return this;
    }

    /// <summary>
    /// Gets the code for a specific provider. Returns null if not explicitly defined.
    /// </summary>
    public string? GetCode(string providerName)
    {
        return ProviderCodes.TryGetValue(providerName, out var code) ? code : null;
    }

    public override bool Equals(object? obj)
    {
        return obj is LanguageDefinition other && Id == other.Id;
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }
}
