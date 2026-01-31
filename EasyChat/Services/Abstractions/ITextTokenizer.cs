using System.Collections.Generic;
using EasyChat.Models;
using EasyChat.Models.Translation.Selection;

namespace EasyChat.Services.Abstractions;

public interface ITextTokenizer
{
    IEnumerable<TextToken> Tokenize(string text);
}
