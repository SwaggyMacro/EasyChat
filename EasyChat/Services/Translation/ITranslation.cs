using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EasyChat.Services.Languages;

namespace EasyChat.Services.Translation;

public interface ITranslation
{
    Task<string> TranslateAsync(string text, LanguageDefinition source, LanguageDefinition destination, bool showOriginal = false,
        CancellationToken cancellationToken = default);

    async IAsyncEnumerable<string> StreamTranslateAsync(string text, LanguageDefinition source, LanguageDefinition destination,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var result = await TranslateAsync(text, source, destination, false, cancellationToken);
        yield return result;
    }
}