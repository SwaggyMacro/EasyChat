using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

/// <summary>
/// Configuration class for managing translation prompts.
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class Prompts : ReactiveObject
{
    private ObservableCollection<PromptEntry> _entries = [];

    /// <summary>
    /// Default translation prompt content.
    /// </summary>
    public const string DefaultPromptContent =
        """
        # Role
        You are a master translator proficient in [SourceLang] and [TargetLang]. You adhere to the principles of "Accuracy, Fluency, and Elegance".
        
        # Response Protocol
        For every message the user sends, you must perform the following actions:
        1.  **Analyze**: Treat the user's message as [SourceLang] content to be translated.
        2.  **Internal Processing (SILENT)**:
            * Step 1: Translate literally to preserve meaning.
            * Step 2: Critique for grammar, tone, and cultural nuance.
            * Step 3: Polish for native-level elegance.
        3.  **Execute**: Output ONLY the final result from Step 3.
        
        # Strict Output Constraints
        * **NO conversational filler**: Do not say "Here is the translation", "Sure", or "Step 1".
        * **NO meta-data**: Do not explain your process.
        * **Direct Output**: Your response must start directly with the translated text.
        * **Format**: Plain text. Keep original code blocks/LaTeX unchanged.
        
        # Interaction Example
        User: [Content]
        Assistant: [Translated Content]
        """;

    public Prompts()
    {
        // Initialize with default prompt if empty
        if (_entries.Count == 0)
        {
            _entries.Add(new PromptEntry
            {
                Name = "Default",
                Content = DefaultPromptContent,
                IsDefault = true
            });
        }
    }

    /// <summary>
    /// The ID of the currently selected prompt.
    /// </summary>
    [JsonProperty]
    public string SelectedPromptId
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    /// <summary>
    /// Collection of all prompt entries.
    /// </summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public ObservableCollection<PromptEntry> Entries
    {
        get => _entries;
        set => this.RaiseAndSetIfChanged(ref _entries, value);
    }

    [OnDeserialized]
    internal void OnDeserializedMethod(StreamingContext context)
    {
        // Deduplicate identical entries (same name, content, and default status)
        // This fixes the issue where default prompts were duplicated due to earlier configuration bugs
        var distinctItems = Entries
            .GroupBy(x => new { x.Name, x.Content, x.IsDefault })
            .Select(g => g.First())
            .ToList();

        if (distinctItems.Count < Entries.Count)
        {
            Entries = new ObservableCollection<PromptEntry>(distinctItems);
        }
    }

    /// <summary>
    /// Gets the currently active prompt content.
    /// Returns the default prompt content if no prompt is selected.
    /// </summary>
    [JsonIgnore]
    public string ActivePromptContent
    {
        get
        {
            // Find by ID first
            if (!string.IsNullOrEmpty(SelectedPromptId))
            {
                var selected = FindById(SelectedPromptId);
                if (selected != null) return selected.Content;
            }

            // Fallback to default
            foreach (var entry in Entries)
            {
                if (entry.IsDefault) return entry.Content;
            }

            // Fallback to hardcoded default
            return DefaultPromptContent;
        }
    }

    /// <summary>
    /// Find a prompt entry by its ID.
    /// </summary>
    public PromptEntry? FindById(string id)
    {
        foreach (var entry in Entries)
        {
            if (entry.Id == id) return entry;
        }
        return null;
    }
}
