using EasyChat.Models.Configuration;
using EasyChat.Services.Abstractions;
using EasyChat.Services.Translation;
using EasyChat.Services.Translation.Selection;

namespace EasyChat.Services.Shortcuts.Handlers;

/// <summary>
/// Handler for the SelectionTranslate shortcut action.
/// Performs translation on the currently selected text.
/// </summary>
public class SelectionTranslateHandler : IShortcutActionHandler
{
    private readonly SelectionTranslationService _selectionTranslationService;

    public string ActionType => "SelectionTranslate";
    
    public SelectionTranslateHandler(SelectionTranslationService selectionTranslationService)
    {
        _selectionTranslationService = selectionTranslationService;
    }

    public void Execute(ShortcutParameter? parameter = null)
    {
        // Fire and forget
        _ = _selectionTranslationService.TranslateCurrentSelectionAsync();
    }
}
