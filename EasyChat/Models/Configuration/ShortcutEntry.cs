using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

/// <summary>
///     Represents a single shortcut entry with action type, optional parameter, and key combination.
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class ShortcutEntry : ReactiveObject
{
    [JsonProperty]
    public string ActionType
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "Screenshot";

    [JsonProperty]
    public ShortcutParameter? Parameter
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    [JsonProperty]
    public string KeyCombination
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    [JsonProperty]
    public bool IsEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    /// <summary>
    ///     Gets the display text for this entry.
    /// </summary>
    public string DisplayText
    {
        get
        {
            var def = GetActionDefinition();
            if (def == null) return ActionType;
            
            if (def.RequiresParameter && Parameter != null)
            {
                if (!string.IsNullOrEmpty(Parameter.Value))
                {
                     return $"{def.DisplayName} ({Parameter.Value})";
                }

                var paramStr = Parameter.Engine;
                if (Parameter.Source != null && Parameter.Target != null)
                {
                    paramStr += $" ({Parameter.Source.DisplayName} -> {Parameter.Target.DisplayName})";
                }
                return $"{def.DisplayName} - {paramStr}";
            }
            
            return def.DisplayName;
        }
    }

    /// <summary>
    ///     Gets the parameter display text (without action name) for this entry.
    /// </summary>
    public string ParameterDisplayText
    {
        get
        {
            if (Parameter == null) return string.Empty;
            
            if (!string.IsNullOrEmpty(Parameter.Value))
            {
                return Parameter.Value;
            }

            var paramStr = Parameter.Engine;
            if (Parameter.Source != null && Parameter.Target != null)
            {
                paramStr += $" ({Parameter.Source.DisplayName} -> {Parameter.Target.DisplayName})";
            }
            return paramStr;
        }
    }

    /// <summary>
    ///     Gets the action definition for this entry.
    /// </summary>
    public IShortcutActionDefinition? GetActionDefinition()
    {
        return ShortcutActionDefinition.GetByType(ActionType);
    }
}