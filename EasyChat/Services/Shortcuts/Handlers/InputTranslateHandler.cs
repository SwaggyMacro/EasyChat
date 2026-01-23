using Avalonia.Threading;
using EasyChat.Models.Configuration;
using EasyChat.Services.Abstractions;
using EasyChat.Views.Typing;

namespace EasyChat.Services.Shortcuts.Handlers;

/// <summary>
/// Handler for the InputTranslate shortcut action.
/// Opens the TypingView for manual text input translation.
/// </summary>
public class InputTranslateHandler : IShortcutActionHandler
{
    private readonly IPlatformService _platformService;

    public string ActionType => "InputTranslate";

    public InputTranslateHandler(IPlatformService platformService)
    {
        _platformService = platformService;
    }

    public void Execute(ShortcutParameter? parameter = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var hwnd = _platformService.GetForegroundWindowHandle();
            var typingView = new TypingView(hwnd);
            typingView.Show();
        });
    }
}
