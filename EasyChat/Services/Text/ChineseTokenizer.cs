using System.Collections.Generic;
using System.Text.RegularExpressions;
using EasyChat.Models.Translation.Selection;
using EasyChat.Services.Abstractions;

namespace EasyChat.Services.Text;

public class ChineseTokenizer : ITextTokenizer
{
    // Regex strategy:
    // 1. Keep ASCII/basic latin words together: [a-zA-Z0-9]+
    // 2. Treat whitespace as token: \s+
    // 3. Treat everything else (Hanzi, punctuation) as individual characters
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
            // Determine if it is a "word" that should have hover effect/selection
            // For Chinese, usually each character is a word-concept, so we treat Hanzi as IsWord=true.
            // Punctuation should be false.
            // ASCII words: true.
            
            bool isWord = false;
            if (char.IsLetterOrDigit(val[0]))
            {
                // Covers 'Hello' and '你' (IsLetter returns true for Hanzi)
                isWord = true;
            }
            
            // Check for punctuation more specifically if needed, 
            // but char.IsLetterOrDigit is usually good enough to exclude punctuation.

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
