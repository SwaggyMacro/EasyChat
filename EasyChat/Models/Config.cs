using EasyChat.Models.Configuration;

namespace EasyChat.Models;

public class Config
{
    public General GeneralConf { get; set; } = new();
    public AiModel AiModelConf { get; set; } = new();
    public MachineTrans MachineTransConf { get; set; } = new();
    public Proxy ProxyConf { get; set; } = new();
    public Shortcut ShortcutConf { get; set; } = new();
}