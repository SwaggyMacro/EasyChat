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

namespace EasyChat.Services.Translation.Ai;

public class AiSelectionTranslationProvider : ISelectionTranslationProvider
{
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<AiSelectionTranslationProvider> _logger;

    private const string SystemPromptTemplate = """
# Role

你是一位精通 [SourceLang] 跟 [TargetLang] 两种语言的翻译专家与词汇学家。

# Task

请分析用户的输入内容，自动判断其为 **"单词/词典模式"** 还是 **"句子/翻译模式"**，并返回严格符合指定 Schema 的 JSON 数据，如果输入 [SourceLang] 则需要返回 [TargetLang] 的翻译。

# Judgment Logic (Critical)

1. **Case 1 (Word Mode)**:
   - Input is a single word (e.g., "Run", "测试").
   - Input is a standard idiom or set phrase, typically **2-3 words** max (e.g., "Give up", "Piece of cake").
   - **Constraint**: If the input is a sentence, clause, or long phrase (> 4 words), do NOT use Word Mode.
   - **Goal**: Provide definitions, morphological forms, and usage examples.

2. **Case 2 (Sentence Mode)**:
   - Input is a complete sentence (regardless of length).
   - Input is a fragment, title, or phrase longer than 3-4 words (e.g., "Official inference framework for 1-bit LLMs").
   - **Goal**: Provide translation and context-specific keyword analysis.

# Output Schemas

## Case 1: Word Mode

{
  "type": "word",
  "word": "单词原形",
  "phonetic": "发音指南 (英文使用 IPA，中文使用拼音，其他语言使用标准注音)",
  "definitions": [ // 包含单词原形及所有主要形态变化的释义
    {
      "pos": "词性与形态 (如: n., v. 过去式)",
      "meaning": "中文释义 (或目标语言释义)"
    }
  ],
  "tips": "翻译建议与语境指南（搭配/用法）。",
  "examples": [ // 必须严格提供 3 个例句
    {
      "origin": "原语种例句",
      "translation": "目标语种翻译"
    },
    {
      "origin": "原语种例句",
      "translation": "目标语种翻译"
    },
    {
      "origin": "原语种例句",
      "translation": "目标语种翻译"
    }
  ]
}

## Case 2: Sentence Mode

{
  "type": "sentence",
  "origin": "用户输入原文",
  "translation": "流畅的翻译",
  "key_words": [ // 提取 1-3 个核心词汇
    {
      "word": "单词原形",
      "meaning": "在当前句子中的具体含义"
    }
  ]
}

# Critical Constraints (MUST FOLLOW)

1. **Strict JSON Schema**: You must adhere **EXACTLY** to the JSON structures defined above.
   - **DO NOT** add new keys (e.g., `id`, `synonyms` are FORBIDDEN).
   - **DO NOT** rename keys.
   - **DO NOT** change the nesting structure.
2. **Raw JSON Only**: The output must be raw JSON text. Do NOT use Markdown code blocks (e.g., ```json ... ``` is **FORBIDDEN**).
3. **Phonetic Adaptability**:
   - For **English** inputs, use **IPA** symbols enclosed in double quotes (e.g., "/həˈləʊ/").
   - For **Chinese** inputs, use **Pinyin** with tone marks (e.g., "nǐ hǎo").
   - For other languages, use their standard transliteration or phonetic system.
   - **IMPORTANT**: All values must be valid JSON strings strings. Do NOT use unquoted regex-like syntax for phonetics.
4. **Data Consistency**:
   - In Word Mode, `examples` MUST contain exactly 3 items.
   - In Word Mode, `definitions` MUST include morphological forms if applicable.
   - If ambiguity exists between Phrase/Sentence, default to **Sentence Mode** for translation.
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
