using System.Collections.Generic;
using System.Linq;
using EasyChat.Lang;
using EasyChat.Services.Abstractions;

namespace EasyChat.Models.Configuration;
/// <summary>
///     Interface for shortcut action definitions.
/// </summary>
public interface IShortcutActionDefinition
{
    string ActionType { get; }
    string ResourceKey { get; }
    string DisplayName { get; }
    bool RequiresParameter { get; }
    string? ParameterHintKey { get; }
    string? ParameterHint { get; }
    
    IEnumerable<string>? GetParameterOptions(IConfigurationService config);
}

/// <summary>
///     Base implementation of IShortcutActionDefinition.
/// </summary>
public abstract class ShortcutActionDefinition : IShortcutActionDefinition
{
    public required string ActionType { get; init; }
    public required string ResourceKey { get; init; }
    public bool RequiresParameter { get; init; }
    public string? ParameterHintKey { get; init; }

    public string DisplayName => Resources.ResourceManager.GetString(ResourceKey, Resources.Culture) ?? ResourceKey;

    public string? ParameterHint => !string.IsNullOrEmpty(ParameterHintKey)
        ? Resources.ResourceManager.GetString(ParameterHintKey, Resources.Culture) ?? ParameterHintKey
        : null;

    public abstract IEnumerable<string>? GetParameterOptions(IConfigurationService config);

    public static readonly IShortcutActionDefinition[] AvailableActions =
    [
        new SimpleActionDefinition
        {
            ActionType = "Screenshot",
            ResourceKey = "Action_ScreenshotTranslate"
        },
        new SimpleActionDefinition
        {
            ActionType = "InputTranslate",
            ResourceKey = "Action_InputTranslate"
        },
        new SimpleActionDefinition
        {
            ActionType = "SelectionTranslate",
            ResourceKey = "Action_SelectionTranslate"
        },
        new StaticOptionsActionDefinition
        {
            ActionType = "SwitchSourceLang",
            ResourceKey = "Action_SwitchSourceLang",
            RequiresParameter = true,
            Options = ["zh", "en", "ja", "ko", "fr", "de", "es", "ru"],
            ParameterHintKey = "Hint_TargetLangCode"
        },
        new StaticOptionsActionDefinition
        {
            ActionType = "SwitchTargetLang",
            ResourceKey = "Action_SwitchTargetLang",
            RequiresParameter = true,
            Options = ["zh", "en", "ja", "ko", "fr", "de", "es", "ru"],
            ParameterHintKey = "Hint_TargetLangCode"
        },
        new SwitchEngineSourceTargetActionDefinition
        {
            ActionType = "SwitchEngineSourceTarget",
            ResourceKey = "Action_SwitchEngineSourceTarget",
            RequiresParameter = true,
            ParameterHintKey = "Hint_SwitchConfig"
        }
    ];

    public static IShortcutActionDefinition? GetByType(string actionType)
    {
        return AvailableActions.FirstOrDefault(a => a.ActionType == actionType);
    }
}

public class SimpleActionDefinition : ShortcutActionDefinition
{
    public override IEnumerable<string>? GetParameterOptions(IConfigurationService config) => null;
}

public class StaticOptionsActionDefinition : ShortcutActionDefinition
{
    public required string[] Options { get; init; }

    public override IEnumerable<string> GetParameterOptions(IConfigurationService config) => Options;
}

public class SwitchEngineSourceTargetActionDefinition : ShortcutActionDefinition
{
    public override IEnumerable<string>? GetParameterOptions(IConfigurationService config)
    {
        // Free text input expected: Engine,SourceID,TargetID
        return null; 
    }
}