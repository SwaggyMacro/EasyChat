using System.Collections.ObjectModel;
using Newtonsoft.Json;

namespace EasyChat.Models.Configuration;

/// <summary>
///     Shortcut configuration containing a list of shortcut entries.
/// </summary>
[JsonObject(MemberSerialization.OptIn)]
public class Shortcut
{
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public ObservableCollection<ShortcutEntry> Entries { get; set; } = new();
}