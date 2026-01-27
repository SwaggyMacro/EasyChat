using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EasyChat.Services.Speech;

public class SubtitleProcessor
{
    private static readonly char[] Punctuations = { '.', '?', '!', '。', '？', '！' };

    /// <summary>
    /// Splits text into sentences based on punctuation, keeping punctuation attached to the preceding segment.
    /// </summary>
    public IEnumerable<string> SplitSentences(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        
        // Pattern: (?<=[.?!。？！]) matches a position where the preceding character is one of the punctuations.
        var parts = Regex.Split(text, @"(?<=[.?!。？！])");
        foreach (var p in parts)
        {
            if (!string.IsNullOrWhiteSpace(p)) yield return p.Trim();
        }
    }

    /// <summary>
    /// Counts the number of sentences in the text based on punctuation.
    /// </summary>
    public int CountSentences(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return text.Count(c => Punctuations.Contains(c));
    }

    /// <summary>
    /// Processes a partial result string to strictly limit the number of visible sentences.
    /// Returns the last N sentences if the total count exceeds maxSentences.
    /// </summary>
    public string ProcessPartialDisplay(string fullText, int maxSentences)
    {
        if (string.IsNullOrEmpty(fullText)) return "";

        var partialSegments = SplitSentences(fullText).ToList();

        if (partialSegments.Count <= maxSentences)
        {
            return fullText;
        }

        // Take last N
        var visibleSegments = partialSegments.Skip(partialSegments.Count - maxSentences);
        return string.Join(" ", visibleSegments);
    }

    /// <summary>
    /// Splits the full text into paragraphs (chunks of sentences) based on the maxSentencesPerParagraph limit.
    /// </summary>
    public List<string> SplitIntoParagraphs(string fullText, int maxSentencesPerParagraph)
    {
        if (string.IsNullOrEmpty(fullText)) return new List<string>();
        
        var segments = SplitSentences(fullText).ToList();
        var paragraphs = new List<string>();
        
        for (int i = 0; i < segments.Count; i += maxSentencesPerParagraph)
        {
            var chunk = segments.Skip(i).Take(maxSentencesPerParagraph);
            paragraphs.Add(string.Join(" ", chunk));
        }

        if (paragraphs.Count == 0 && !string.IsNullOrWhiteSpace(fullText))
        {
            paragraphs.Add(fullText); // Fallback if no valid segments but text exists
        }
        
        return paragraphs;
    }
}
