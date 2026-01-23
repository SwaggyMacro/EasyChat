using System;
using EasyChat.Services.Languages;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DeepL;
using Microsoft.Extensions.Logging;

namespace EasyChat.Services.Translation.Machine;

public class DeepLService : ITranslation
{
    private readonly ILogger<DeepLService> _logger;
    private readonly ModelType _modelType;
    private readonly Translator _translator;

    public DeepLService(string modelType, string apiKey, string? proxy, ILogger<DeepLService> logger)
    {
        _logger = logger;
        _modelType = modelType switch
        {
            "quality_optimized" => ModelType.QualityOptimized,
            "prefer_quality_optimized" => ModelType.PreferQualityOptimized,
            "latency_optimized" => ModelType.LatencyOptimized,
            _ => ModelType.LatencyOptimized
        };

        if (proxy != null)
        {
            var handler = new HttpClientHandler
            {
                Proxy = new WebProxy(proxy),
                UseProxy = true
            };
            var options = new TranslatorOptions
            {
                ClientFactory = () => new HttpClientAndDisposeFlag
                {
                    HttpClient = new HttpClient(handler),
                    DisposeClient = true
                }
            };
            _translator = new Translator(apiKey, options);
        }
        else
        {
            _translator = new Translator(apiKey);
        }
        
        _logger.LogDebug("DeepLService initialized: ModelType={ModelType}", modelType);
    }

    public async Task<string> TranslateAsync(string text, LanguageDefinition source, LanguageDefinition destination, bool showOriginal = true,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Translation request: {Source} → {Dest}, Length={Length}", source.DisplayName, destination.DisplayName, text.Length);
        
        var src = source.GetCode("DeepL") ?? source.Id;
        var dest = destination.GetCode("DeepL") ?? destination.Id;
        
        try
        {
            var result = await _translator.TranslateTextAsync(
                text,
                src,
                dest,
                new TextTranslateOptions { ModelType = _modelType },
                cancellationToken);
            
            _logger.LogDebug("Translation completed: ResultLength={Length}", result.Text.Length);
            return result.Text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Translation failed");
            throw;
        }
    }
}