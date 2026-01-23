using System;
using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

/// <summary>
/// Represents a single prompt configuration entry.
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class PromptEntry : ReactiveObject
{
    private string _id = Guid.NewGuid().ToString();
    private string _name = string.Empty;
    private string _content = string.Empty;
    private bool _isDefault;

    /// <summary>
    /// Unique identifier for the prompt entry.
    /// </summary>
    [JsonProperty]
    public string Id
    {
        get => _id;
        set => this.RaiseAndSetIfChanged(ref _id, value);
    }

    /// <summary>
    /// Display name for the prompt.
    /// </summary>
    [JsonProperty]
    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    /// <summary>
    /// The prompt content/template text.
    /// </summary>
    [JsonProperty]
    public string Content
    {
        get => _content;
        set
        {
            this.RaiseAndSetIfChanged(ref _content, value);
            this.RaisePropertyChanged(nameof(ContentPreview));
        }
    }

    /// <summary>
    /// Whether this is the default prompt to use.
    /// </summary>
    [JsonProperty]
    public bool IsDefault
    {
        get => _isDefault;
        set => this.RaiseAndSetIfChanged(ref _isDefault, value);
    }

    /// <summary>
    /// Gets a truncated preview of the content for display purposes.
    /// </summary>
    [JsonIgnore]
    public string ContentPreview =>
        string.IsNullOrEmpty(Content)
            ? string.Empty
            : Content.Length > 100
                ? Content.Substring(0, 100) + "..."
                : Content;
}
