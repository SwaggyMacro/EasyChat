using System;
using EasyChat.Services.Languages;
using System.Collections.Generic;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace EasyChat.Services.Translation.Ai;

public class OpenAiService : ITranslation
{
    private readonly string _apiKey;
    private readonly string _apiUrl;
    private readonly ILogger<OpenAiService> _logger;
    private readonly string _model;
    private readonly string? _proxy;
    private readonly string _promptTemplate;

    public OpenAiService(string apiUrl, string apiKey, string model, string? proxy, string promptTemplate, ILogger<OpenAiService> logger)
    {
        _apiUrl = apiUrl;
        _apiKey = apiKey;
        _model = model;
        _proxy = proxy;
        _promptTemplate = promptTemplate;
        _logger = logger;
        
        _logger.LogDebug("OpenAiService initialized: Model={Model}", model);
    }

    private string GetPrompt(LanguageDefinition source, LanguageDefinition destination)
    {
        var src = !string.IsNullOrEmpty(source.EnglishName) ? source.EnglishName : source.Id;
        var dest = !string.IsNullOrEmpty(destination.EnglishName) ? destination.EnglishName : destination.Id;
        
        return _promptTemplate
            .Replace("[SourceLang]", src, StringComparison.OrdinalIgnoreCase)
            .Replace("[TargetLang]", dest, StringComparison.OrdinalIgnoreCase)
            .Replace("[源语言]", src, StringComparison.OrdinalIgnoreCase)
            .Replace("[目标语言]", dest, StringComparison.OrdinalIgnoreCase);
    }

    private ChatClient CreateClient()
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(_apiUrl)
        };

        if (!string.IsNullOrWhiteSpace(_proxy))
        {
            var handler = new HttpClientHandler
            {
                Proxy = new WebProxy(_proxy),
                UseProxy = true
            };
            options.Transport = new HttpClientPipelineTransport(new HttpClient(handler));
        }

        var client = new OpenAIClient(new ApiKeyCredential(_apiKey), options);
        return client.GetChatClient(_model);
    }

    public async Task<string> TranslateAsync(string text, LanguageDefinition? source, LanguageDefinition? destination, bool showOriginal = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        _logger.LogDebug("Translation request: {Source} → {Dest}, Length={Length}", source.DisplayName, destination.DisplayName, text.Length);
        
        try
        {
            var client = CreateClient();
            List<ChatMessage> messages =
            [
                new SystemChatMessage(GetPrompt(source, destination)),
                new UserChatMessage(text)
            ];

            ChatCompletion completion = await client.CompleteChatAsync(messages, cancellationToken: cancellationToken);
            
            // Combine all content parts if multiple
            string result = completion.Content.Count > 0 ? completion.Content[0].Text : string.Empty;

            _logger.LogDebug("Translation completed: ResultLength={Length}", result.Length);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation failed");
            throw;
        }
    }

    public async IAsyncEnumerable<string> StreamTranslateAsync(string text, LanguageDefinition? source, LanguageDefinition? destination,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        _logger.LogDebug("Stream translation request: {Source} → {Dest}, Length={Length}", source.DisplayName, destination.DisplayName, text.Length);
        
        var client = CreateClient();
        List<ChatMessage> messages =
        [
            new SystemChatMessage(GetPrompt(source, destination)),
            new UserChatMessage(text)
        ];

        await foreach (var update in client.CompleteChatStreamingAsync(messages, cancellationToken: cancellationToken))
        {
            if (update.ContentUpdate.Count > 0)
            {
                yield return update.ContentUpdate[0].Text;
            }
        }
    }
}