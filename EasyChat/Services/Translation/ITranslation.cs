using System.Threading;
using System.Threading.Tasks;
using EasyChat.Services.Languages;

namespace EasyChat.Services.Translation;

public interface ITranslation
{
    Task<string> TranslateAsync(string text, LanguageDefinition source, LanguageDefinition destination, bool showOriginal = false,
        CancellationToken cancellationToken = default);
}