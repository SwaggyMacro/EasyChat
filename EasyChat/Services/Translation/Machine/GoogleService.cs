using System;
using EasyChat.Services.Languages;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EasyChat.Lang;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace EasyChat.Services.Translation.Machine;

public class GoogleService : ITranslation, IDisposable
{
    private const string BaseUrl = "https://translation.googleapis.com/";
    private const string Endpoint = "language/translate/v2/";
    private readonly RestClient _client;
    private readonly string _key;
    private readonly ILogger<GoogleService> _logger;

    private readonly string _model;

    public GoogleService(string model, string key, string? proxy, ILogger<GoogleService> logger)
    {
        _model = model;
        _key = key;
        _logger = logger;

        var options = new RestClientOptions(BaseUrl);
        if (proxy != null) options.Proxy = new WebProxy(proxy);
        _client = new RestClient(options);
        
        _logger.LogDebug("GoogleService initialized: Model={Model}", model);
    }

    public void Dispose()
    {
        _client.Dispose();
        _logger.LogDebug("GoogleService disposed");
    }

    public async Task<string> TranslateAsync(string text, LanguageDefinition source, LanguageDefinition destination, bool showOriginal = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Translation request: {Source} → {Dest}, Length={Length}", source.DisplayName, destination.DisplayName, text.Length);
        
        var src = source.GetCode("Google") ?? source.Id;
        var dest = destination.GetCode("Google") ?? destination.Id;
        
        var translatedResult = await TranslateInternalAsync(text, src, dest, cancellationToken);
        if (showOriginal) return translatedResult ?? Resources.RequestError;

        if (translatedResult == null)
        {
            _logger.LogWarning("Translation failed: null response");
            return Resources.RequestError;
        }

        var jObject = JObject.Parse(translatedResult);

        if (jObject.ContainsKey("error"))
        {
            _logger.LogWarning("API error: {Response}", translatedResult);
            return translatedResult;
        }
        
        var result = jObject["data"]!["translations"]![0]!["translatedText"]!.ToString();
        _logger.LogDebug("Translation completed: ResultLength={Length}", result.Length);
        return result;
    }

    private async Task<string?> TranslateInternalAsync(string text, string source, string destination,
        CancellationToken cancellationToken)
    {
        var request = new RestRequest(Endpoint);
        request.AddParameter("key", _key);
        request.AddParameter("q", text);
        request.AddParameter("source", source);
        request.AddParameter("target", destination);
        request.AddParameter("model", _model);

        var response = await _client.ExecuteAsync(request, cancellationToken);
        return response.Content;
    }
}