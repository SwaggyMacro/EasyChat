using System.Collections.Generic;
using System.Text.RegularExpressions;
using EasyChat.Models.Translation.Selection;
using EasyChat.Services.Abstractions;

namespace EasyChat.Services.Text;

public class JapaneseTokenizer : ITextTokenizer
{
    // Similar to Chinese, we split by character for simplicity.
    // Kana strings "might" be better grouped, but simple character tokenization is robust.
    // 1. ASCII/Latin words: Grouped
    // 2. Whitespace: Grouped
    // 3. Characters (Kanji, Kana, Punctuation): Individual
    private static readonly Regex TokenRegex = new(@"([a-zA-Z0-9]+)|(\s+)|(.)", RegexOptions.Compiled);

    public IEnumerable<TextToken> Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var matches = TokenRegex.Matches(text);
        foreach (Match match in matches)
        {
            var val = match.Value;
            
            bool isWord = false;
            // IsLetterOrDigit covers Kanji and Kana
            if (char.IsLetterOrDigit(val[0]))
            {
                isWord = true;
            }

            yield return new TextToken
            {
                Text = val,
                IsWord = isWord,
                StartIndex = match.Index,
                Length = match.Length
            };
        }
    }
}
