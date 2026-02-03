using System.Diagnostics.CodeAnalysis;
using EasyChat.Models.Configuration;

namespace EasyChat.Models;

[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public class Config
{
    public General GeneralConf { get; set; } = new();
    public AiModel AiModelConf { get; set; } = new();
    public MachineTrans MachineTransConf { get; set; } = new();
    public Proxy ProxyConf { get; set; } = new();
    public Shortcut ShortcutConf { get; set; } = new();
    public ScreenshotConfig ScreenshotConf { get; set; } = new();
    public SelectionTranslationConfig SelectionTranslationConf { get; set; } = new();
    public TtsConfig TtsConf { get; set; } = new();
}