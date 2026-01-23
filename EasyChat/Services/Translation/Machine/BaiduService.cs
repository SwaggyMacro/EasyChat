using System;
using EasyChat.Services.Languages;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EasyChat.Lang;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace EasyChat.Services.Translation.Machine;

public class BaiduService : ITranslation, IDisposable
{
    private const string BaseUrl = "https://api.fanyi.baidu.com/";
    private const string Endpoint = "api/trans/vip/translate";
    public static readonly string Chinese = "zh";
    public static readonly string English = "en";

    private readonly string _appId;
    private readonly RestClient _client;
    private readonly ILogger<BaiduService> _logger;
    private readonly string _secretKey;

    public BaiduService(string appId, string secretKey, string? proxy, ILogger<BaiduService> logger)
    {
        _appId = appId;
        _secretKey = secretKey;
        _logger = logger;

        var options = new RestClientOptions(BaseUrl);
        if (proxy != null) options.Proxy = new WebProxy(proxy);
        _client = new RestClient(options);
        
        _logger.LogDebug("BaiduService initialized");
    }

    public void Dispose()
    {
        _client.Dispose();
        _logger.LogDebug("BaiduService disposed");
    }

    public async Task<string> TranslateAsync(string text, LanguageDefinition source, LanguageDefinition destination, bool showOriginal = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Translation request: {Source} → {Dest}, Length={Length}", source.DisplayName, destination.DisplayName, text.Length);
        
        var src = source.GetCode("Baidu") ?? source.Id;
        var dest = destination.GetCode("Baidu") ?? destination.Id;
        
        var translatedResult = await TranslateInternalAsync(text, src, dest, cancellationToken);
        if (showOriginal) return translatedResult ?? Resources.RequestError;

        if (translatedResult == null)
        {
            _logger.LogWarning("Translation failed: null response");
            return Resources.RequestError;
        }

        var jObject = JObject.Parse(translatedResult);
        var resultText = "";

        if (jObject.ContainsKey("error_msg"))
        {
            _logger.LogWarning("API error: {Response}", translatedResult);
            resultText = translatedResult;
        }
        else
        {
            var transResult = jObject["trans_result"];
            if (transResult != null)
                resultText = transResult.Aggregate(resultText, (current, item) => current + item["dst"]);
            
            _logger.LogDebug("Translation completed: ResultLength={Length}", resultText.Length);
        }

        return resultText;
    }

    private async Task<string?> TranslateInternalAsync(string text, string source, string destination,
        CancellationToken cancellationToken)
    {
        var salt = Random.Shared.Next(100000).ToString();
        var sign = ComputeMd5Hash(_appId + text + salt + _secretKey);

        var request = new RestRequest(Endpoint);
        request.AddParameter("q", text);
        request.AddParameter("from", source);
        request.AddParameter("to", destination);
        request.AddParameter("appid", _appId);
        request.AddParameter("salt", salt);
        request.AddParameter("sign", sign);

        var response = await _client.ExecuteAsync(request, cancellationToken);
        return response.Content;
    }

    private static string ComputeMd5Hash(string input)
    {
        var hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hashBytes);
    }
}