using System.ComponentModel;

namespace EasyChat.Models.Configuration;

public enum WindowClosingBehavior
{
    [Description("Ask Every Time")]
    Ask = 0,
    
    [Description("Exit Application")]
    ExitApp = 1,
    
    [Description("Minimize to Tray")]
    MinimizeToTray = 2
}
