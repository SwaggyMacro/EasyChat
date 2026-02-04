using System.Collections.Generic;
using System.Text.RegularExpressions;
using EasyChat.Models.Translation.Selection;
using EasyChat.Services.Abstractions;

namespace EasyChat.Services.Text;

/// <summary>
/// A fallback tokenizer that attempts to handle any language reasonably well.
/// It uses a mixed strategy:
/// - CJK characters are treated as individual words.
/// - Latin/Cyrillic/Greek sequences are treated as space-delimited words.
/// - Whitespace and punctuation are handled normally.
/// </summary>
public class UniversalTokenizer : ITextTokenizer
{
    // CJK Ranges (simplified): \u4e00-\u9fff (Common), \u3000-\u303f (Punctuation), \u3040-\u309f (Hiragana), \u30a0-\u30ff (Katakana)
    // We want to treat these as individual tokens mostly, or at least break on them.
    // 
    // Regex Strategy:
    // 1. Strings of non-CJK word chars (Latin, Cyrillic, Numbers, etc.): [^\u2E80-\u9FFF\s\p{P}]+ 
    //    (Actually simpler: \w+ but excluding CJK if \w matches them? \w usually matches CJK in .NET)
    //    
    // Simpler approach compatible with SpaceBased + CJK:
    // Match either:
    // A. A CJK character (treated singly)
    // B. A sequence of non-CJK alphanumeric characters
    // C. Whitespace
    // D. Punctuation/Other
    
    // Unicode Blocks: IsCJKUnifiedIdeographs, IsHiragana, IsKatakana, IsHangulSyllables
    // Regex \p{Lo} usually matches "Letter, Other" which includes CJK.
    // \p{L} matches all letters.
    
    // Let's use a specific char-loop for maximum control and performance, or a carefully crafted Regex.
    // Regex:
    // ([a-zA-Z0-9_\u00C0-\u024F]+)  -> Latin/West European words (extended)
    // (\s+) -> Whitespace
    // (.) -> Everything else (including CJK chars and punctuation, caught one by one)
    
    // The previous regex used in ChineseTokenizer is actually quite good as a universal fallback:
    // ([a-zA-Z0-9]+)|(\s+)|(.)
    // But we might want to support accented characters in "Words" (e.g. French 'été').
    // So we need \w but split CJK.
    
    // Regex: (\w matches CJK in .NET, so we can't just use \w+)
    // We can rely on specific ranges or 'IsLetter' properties in code.
    
    // Let's stick to the Regex engine for consistency with others:
    // ([a-zA-Z0-9\u00C0-\u017F]+) covers Latin Extended A.
    // But easiest is likely: 
    // Match strict ASCII+Latin words, OR match CJK chars individually.
    
    private static readonly Regex TokenRegex = new(@"([a-zA-Z0-9\u00C0-\u024F\u0400-\u04FF]+)|(\s+)|(.)", RegexOptions.Compiled); 
    // Groups:
    // 1. Latin/Cyrillic words (rudimentary)
    // 2. Whitespace
    // 3. Fallback (single chars, CJK, punctuation)

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

            if (match.Groups[1].Success)
            {
                // It matched the Latin/Cyrillic word group
                isWord = true;
            }
            else if (match.Groups[2].Success)
            {
                // Whitespace
                isWord = false;
            }
            else
            {
                // Fallback group (CJK or Punctuation)
                if (char.IsLetterOrDigit(val[0]))
                {
                    // It's a letter (e.g. CJK Hanzi, Kana) -> IsWord
                    isWord = true;
                }
                // Else punctuation -> Not word
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
