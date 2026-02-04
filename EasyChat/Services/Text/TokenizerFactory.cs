using EasyChat.Services.Abstractions;

namespace EasyChat.Services.Text;

public interface ITokenizerFactory
{
    ITextTokenizer GetTokenizer(string languageCode);
}

public class TokenizerFactory : ITokenizerFactory
{
    // Cache stateless tokenizers to avoid allocation
    private readonly SpaceBasedTokenizer _spaceBasedTokenizer = new();
    private readonly ChineseTokenizer _chineseTokenizer = new();
    private readonly JapaneseTokenizer _japaneseTokenizer = new();
    private readonly UniversalTokenizer _universalTokenizer = new();

    public ITextTokenizer GetTokenizer(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return _universalTokenizer;
        }

        // Normalize
        // "zh-CN" -> "zh", "en-US" -> "en"
        var primaryCode = languageCode.Split('-')[0].ToLowerInvariant();

        return primaryCode switch
        {
            "zh" => _chineseTokenizer,
            "ja" => _japaneseTokenizer,
            "en" => _spaceBasedTokenizer,
            "fr" => _spaceBasedTokenizer,
            "de" => _spaceBasedTokenizer,
            "es" => _spaceBasedTokenizer,
            "it" => _spaceBasedTokenizer,
            "pt" => _spaceBasedTokenizer,
            "ru" => _spaceBasedTokenizer, // SpaceBased handles Cyrillic if \w or regex supports it, but Universal might be safer if SpaceBased is pure ASCII. 
                                          // Let's check SpaceBasedTokenizer regex: @"(\w+)|(\s+)|([^\w\s]+)"
                                          // in .NET \w includes Unicode letters, so it SHOULD support Cyrillic/Greek/Accents correctly.
                                          // So SpaceBased is fine for most alphabet languages.
            
            // CJK
            "ko" => _universalTokenizer, // Korean is space-delimited but has complex blocks. Universal/SpaceBased both kinda work. Universal splits blocks? No, Universal regex splits latin words. 
                                         // If Korean uses spaces, SpaceBased is better? Korean words are space separated. 
                                         // Let's map ko to SpaceBased for now as it's closer to truth than char-based.
            
            _ => _universalTokenizer
        };
    }
}
