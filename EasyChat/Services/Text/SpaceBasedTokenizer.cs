using System.Collections.Generic;
using System.Text.RegularExpressions;
using EasyChat.Models.Translation.Selection;
using EasyChat.Services.Abstractions;

namespace EasyChat.Services.Text;

public class SpaceBasedTokenizer : ITextTokenizer
{
    // Regex to split by words, keeping punctuation as separate tokens
    // \w+ matches word characters
    // \s+ matches whitespace
    // [^\w\s]+ matches punctuation/symbols
    private static readonly Regex TokenRegex = new(@"(\w+)|(\s+)|([^\w\s]+)", RegexOptions.Compiled);

    public IEnumerable<TextToken> Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var matches = TokenRegex.Matches(text);
        foreach (Match match in matches)
        {
            var isWord = char.IsLetterOrDigit(match.Value[0]);

            yield return new TextToken
            {
                Text = match.Value,
                IsWord = isWord,
                StartIndex = match.Index,
                Length = match.Length
            };
        }
    }
}
