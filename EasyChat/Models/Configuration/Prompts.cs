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
    private string _selectedPromptId = string.Empty;
    private ObservableCollection<PromptEntry> _entries = new();

    /// <summary>
    /// Default translation prompt content.
    /// </summary>
    public const string DefaultPromptContent =
        "# Role\nYou are a master translator proficient in [SourceLang] and [TargetLang], with a deep " +
        "cross-cultural background and academic literacy. You adhere to the translation principles of \"Accuracy, Fluency, and Elegance\" (Faithfulness, Expressiveness, and Elegance).\n\n" +
        "# Task\nTranslate the following [SourceLang] content into [TargetLang].\n\n" +
        "# Workflow (Strictly follow these three steps)\nTo ensure the highest quality translation, " +
        "please follow this three-step process. You may display your thinking process, but the final output must be clear and polished.\n\n" +
        "1.  **Step 1: Literal Translation**\n    * Translate the text faithfully to the literal meaning." +
        "\n    * Preserve the original sentence structure to ensure no information is omitted.\n\n" +
        "2.  **Step 2: Reflection & Critique**\n    * Review the result from Step " +
        "1. Identify parts that are ungrammatical, unnatural, or suffer from \"translationese.\"\n    " +
        "* Verify if professional terms are accurate and if idioms have been properly localized.\n    " +
        "* Consider the cultural background of the target audience and assess if the tone is appropriate.\n\n" +
        "3.  **Step 3: Polished Translation**\n    * Based on the reflection in Step 2, rephrase and refine the text.\n    " +
        "* Optimize sentence structure to make it read as if written by a native speaker of the [TargetLang], without altering the original meaning.\n    " +
        "* Ensure logical flow and use precise, elegant vocabulary.\n\n# Constraints\n* Do not omit any information from the original text.\n" +
        "* Keep code blocks, proper nouns, or specific technical notation (such as LaTeX formulas) unchanged or use standard conventions.\n\n\n" +
        "* Do not return any markdown text, only plain text allowed.\n* Only response translation result.";

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
        get => _selectedPromptId;
        set => this.RaiseAndSetIfChanged(ref _selectedPromptId, value);
    }

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
