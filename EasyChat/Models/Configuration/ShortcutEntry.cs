using Newtonsoft.Json;
using ReactiveUI;

namespace EasyChat.Models.Configuration;

/// <summary>
///     Represents a single shortcut entry with action type, optional parameter, and key combination.
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class ShortcutEntry : ReactiveObject
{
    private string _actionType = "Screenshot";

    private bool _isEnabled = true;

    private string _keyCombination = string.Empty;

    private ShortcutParameter? _parameter;

    [JsonProperty]
    public string ActionType
    {
        get => _actionType;
        set => this.RaiseAndSetIfChanged(ref _actionType, value);
    }

    [JsonProperty]
    public ShortcutParameter? Parameter
    {
        get => _parameter;
        set => this.RaiseAndSetIfChanged(ref _parameter, value);
    }

    [JsonProperty]
    public string KeyCombination
    {
        get => _keyCombination;
        set => this.RaiseAndSetIfChanged(ref _keyCombination, value);
    }

    [JsonProperty]
    public bool IsEnabled
    {
        get => _isEnabled;
        set => this.RaiseAndSetIfChanged(ref _isEnabled, value);
    }

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

            var paramStr = Parameter.Engine ?? string.Empty;
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