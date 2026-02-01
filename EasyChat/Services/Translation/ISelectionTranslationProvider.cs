using System.Threading;
using System.Threading.Tasks;
using EasyChat.Models.Translation.Selection;

namespace EasyChat.Services.Translation;

public interface ISelectionTranslationProvider
{
    /// <summary>
    /// Translates the given text using the specified source and target languages.
    /// The provider should automatically detect if it's a word or sentence and return the appropriate result.
    /// </summary>
    Task<SelectionTranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default);
}
