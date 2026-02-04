using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EasyChat.Models.Configuration;
using EasyChat.Models.Translation.Selection;
using EasyChat.Services.Abstractions;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace EasyChat.Services.Translation.Selection;

public class AiSelectionTranslationProvider : ISelectionTranslationProvider
{
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<AiSelectionTranslationProvider> _logger;

    private const string SystemPromptTemplate = """
# Role
You are a professional translator and lexicographer proficient in [SourceLang] and [TargetLang].

# Task
Source Language: [SourceLang]
Target Language: [TargetLang]
If Source Language is "Auto" or "Auto Detect", automatically detect it based on the input text.

1. Analyze the user's input to determine if it is **"Word/Dictionary Mode"** or **"Sentence/Translation Mode"**.
2. Return the result in the specified JSON format.
3. **CRITICAL**: All translations, definitions, meanings, AND grammatical labels MUST be in **[TargetLang]**. Do NOT use Chinese unless [TargetLang] is Chinese.

# Judgment Logic (STRICT HARD RULES)

**Step 1: Input Cleaning**
- Remove leading/trailing whitespace.
- Remove ALL trailing punctuation marks (e.g., "Schema." -> "Schema", "你好！" -> "你好").

**Step 2: Detect Language Type & Apply Rule**

**RULE A: For Space-Based Languages (English, French, Spanish, etc.)**
- **Logic**: Count the spaces inside the cleaned string.
- **IF Spaces == 0**: 
  - **RESULT**: **Word Mode** (ABSOLUTE).
  - (Examples: "Schema", "Translation", "Unbelievable" -> Word Mode).
- **IF Spaces > 0**:
  - **RESULT**: **Sentence Mode**.
  - (Examples: "Look up", "Human rights", "I go home" -> Sentence Mode).

**RULE B: For No-Space Languages (Chinese, Japanese, etc.)**
- **Logic**: Count the character length.
- **IF Length <= 4 characters**:
  - **RESULT**: **Word Mode**.
  - (Examples: "你", "测试", "四字成语" -> Word Mode).
- **IF Length > 4 characters**:
  - **RESULT**: **Sentence Mode**.
  - (Examples: "我今天去超市", "这个东西很好吃" -> Sentence Mode).

**Override**: Do NOT attempt to analyze grammar/meaning to switch modes. Stick strictly to the Space/Length count.

# Output Schemas

## Case 1: Word Mode

{
  "type": "word",
  "detected_source_language": "The detected source language code (e.g. en, zh-CN, ja)",
  "word": "The original word (lemma form if needed)",
  "phonetic": "Phonetic symbol (IPA for English, Pinyin for Chinese, etc.)",
  "forms": [ // Comprehensive list of ALL morphological forms
    {
       "label": "Tense/Form Name translated into [TargetLang] (e.g., '过去式', '复数', 'Comparatif')",
       "word": "The word form"
    }
  ],
  "definitions": [ 
    {
      "pos": "Part of speech (e.g., n., v., adj.)",
      "meaning": "Meaning in [TargetLang]"
    }
  ],
  "tips": "Usage tips, nuances, or grammatical notes in [TargetLang].",
  "examples": [ // EXACTLY 3 examples
    {
      "origin": "Original sentence",
      "translation": "Translation in [TargetLang]"
    },
    {
      "origin": "Original sentence",
      "translation": "Translation in [TargetLang]"
    },
    {
      "origin": "Original sentence",
      "translation": "Translation in [TargetLang]"
    }
  ]
}

## Case 2: Sentence Mode

{
  "type": "sentence",
  "detected_source_language": "The detected source language code (e.g. en, zh-CN, ja)",
  "origin": "Original text",
  "translation": "Fluent translation of the ENTIRE text in [TargetLang]",
  "key_words": [ // Extract 1-3 key terms
    {
      "word": "The original word",
      "meaning": "Specific meaning in this context in [TargetLang]"
    }
  ]
}

# Critical Constraints

1.  **Strict JSON Schema**: You must adhere **EXACTLY** to the JSON structures defined above.
    - **DO NOT** add new keys.
    - **DO NOT** rename keys.
2.  **Raw JSON Only**: Return strictly raw JSON. Do NOT use Markdown code blocks.
3.  **Phonetic Handling**:
    - English: Use standard IPA inside double quotes (e.g., "/həˈləʊ/").
    - Chinese: Use Pinyin with tones.
    - Ensure all JSON strings are properly escaped.
4.  **Completeness**:
    - In Sentence Mode, you MUST translate every sentence in the input.
    - In Word Mode, you MUST list all applicable morphological forms.
5.  **Language Enforcer**:
    - If [TargetLang] is Japanese, the output "meaning", "translation", "tips", and form "labels" MUST be in Japanese.
    - If [TargetLang] is French, they MUST be in French.
    - **Do NOT output Chinese unless [TargetLang] is Chinese.**
6.  **Absolute Compliance Protocol**:
    - Follow the Space/Length rules blindly. 
    - Even if "Hot dog" is a noun phrase, since it has a space, output it as Sentence Mode.
    - Even if "Schema" looks like a title, since it has no space, output it as Word Mode.
""";

    public AiSelectionTranslationProvider(
        IConfigurationService configurationService,
        ILogger<AiSelectionTranslationProvider> logger)
    {
        _configurationService = configurationService;
        _logger = logger;
    }

    private (ChatClient Client, string SourceLang, string TargetLang) CreateClientAndConfig(string sourceLangOverride, string targetLangOverride)
    {
        // 1. Resolve AI Model
        var generalConf = _configurationService.General;
        var selectionConf = _configurationService.SelectionTranslation;
        var aiConf = _configurationService.AiModel;

        if (generalConf == null || aiConf == null || selectionConf == null)
        {
            throw new InvalidOperationException("Configuration not available");
        }

        CustomAiModel? model = null;
        
        // Priority 1: Selection Translation Specific Model
        if (!string.IsNullOrEmpty(selectionConf.AiModelId))
        {
            model = aiConf.ConfiguredModels.FirstOrDefault(m => m.Id == selectionConf.AiModelId);
        }
        
        // Priority 2: General Config Model ID
        if (model == null && !string.IsNullOrEmpty(generalConf.UsingAiModelId))
        {
            model = aiConf.ConfiguredModels.FirstOrDefault(m => m.Id == generalConf.UsingAiModelId);
        }
        
        // Priority 3: General Config Legacy Name
        if (model == null && !string.IsNullOrEmpty(generalConf.UsingAiModel))
        {
            model = aiConf.ConfiguredModels.FirstOrDefault(m => m.Name == generalConf.UsingAiModel);
        }

        if (model == null)
        {
           throw new InvalidOperationException("No active AI model configured");
        }

        // 2. Create Client
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(model.ApiUrl)
        };

        // Proxy
        if (model.UseProxy && _configurationService.Proxy?.ProxyUrl is { } proxyUrl && !string.IsNullOrWhiteSpace(proxyUrl))
        {
             var handler = new HttpClientHandler
            {
                Proxy = new WebProxy(proxyUrl),
                UseProxy = true
            };
            options.Transport = new HttpClientPipelineTransport(new HttpClient(handler));
        }

        var client = new OpenAIClient(new ApiKeyCredential(model.ApiKey), options);
        var chatClient = client.GetChatClient(model.Model);
        
        return (chatClient, sourceLangOverride, targetLangOverride);
    }

    [Experimental("OPENAI001")]
    public async Task<SelectionTranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Translating text: {Text}, {Source} -> {Target}", text, sourceLang, targetLang);

        try
        {
            var (client, src, tgt) = CreateClientAndConfig(sourceLang, targetLang);
            
            var prompt = SystemPromptTemplate
                .Replace("[SourceLang]", src)
                .Replace("[TargetLang]", tgt);

            List<ChatMessage> messages =
            [
                new SystemChatMessage(prompt),
                new UserChatMessage(text)
            ];
            
            var completionOptions = new ChatCompletionOptions
            {
                Temperature = 0.3f, 
                MaxOutputTokenCount = 4000,
                ReasoningEffortLevel = ChatReasoningEffortLevel.Low,
            };

            ChatCompletion completion = await client.CompleteChatAsync(messages, completionOptions, cancellationToken);
            
            var content = completion.Content[0].Text;
            
            _logger.LogDebug("Raw AI Response: {Content}", content);

            // Robust JSON extraction: Find first '{' and last '}'
            var startIndex = content.IndexOf('{');
            var endIndex = content.LastIndexOf('}');
            
            if (startIndex >= 0 && endIndex > startIndex)
            {
                content = content.Substring(startIndex, endIndex - startIndex + 1);
            }
            else
            {
                _logger.LogWarning("No JSON object found in AI response. Raw Content: {Raw}", content);
                throw new InvalidOperationException("AI response did not contain valid JSON");
            }
            
            // Fix common JSON error: unquoted phonetic "/.../"
            // Looks for "phonetic": /.../ and replaces with "phonetic": "/.../"
            try 
            {
                // Regex matches: "phonetic":\s*(/[^/]+/)
                // We want to capture the slash-enclosed part and wrap it in quotes if it's not already
                content = System.Text.RegularExpressions.Regex.Replace(content, @"""phonetic""\s*:\s*(/[^/]+(?<!\\)/)", @"""phonetic"": ""$1""");
            }
            catch { /* Ignore regex errors */ }

            _logger.LogDebug("Processed JSON: {Content}", content);
            
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            try 
            {
                var result = JsonSerializer.Deserialize<SelectionTranslationResult>(content, options);
                return result ?? throw new InvalidOperationException("Failed to deserialize translation result (null)");
            }
            catch (JsonException jsonEx)
            {
                 _logger.LogError(jsonEx, "JSON Deserialization failed. Content: {Content}", content);
                 throw new InvalidOperationException($"JSON Parse Error: {jsonEx.Message}", jsonEx);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI Selection Translation failed");
            throw; 
        }
    }
}
