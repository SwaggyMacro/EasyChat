using System;
using EasyChat.Services.Languages;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EasyChat.Lang;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace EasyChat.Services.Translation.Machine;

public class TencentService : ITranslation, IDisposable
{
    private const string Endpoint = "tmt.tencentcloudapi.com";
    private const int TimeoutSeconds = 5;
    public static readonly string Chinese = "zh";
    public static readonly string English = "en";
    private readonly RestClient _client;
    private readonly ILogger<TencentService> _logger;

    private readonly string _secretId;
    private readonly string _secretKey;

    public TencentService(string secretId, string secretKey, string? proxy, ILogger<TencentService> logger)
    {
        _secretId = secretId;
        _secretKey = secretKey;
        _logger = logger;

        var options = new RestClientOptions();
        if (proxy != null) options.Proxy = new WebProxy(proxy);
        _client = new RestClient(options);
        
        _logger.LogDebug("TencentService initialized");
    }

    public void Dispose()
    {
        _client.Dispose();
        _logger.LogDebug("TencentService disposed");
    }

    public async Task<string> TranslateAsync(string text, LanguageDefinition? source, LanguageDefinition? destination, bool showOriginal = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        _logger.LogDebug("Translation request: {Source} → {Dest}, Length={Length}", source.DisplayName, destination.DisplayName, text.Length);
        
        var src = source.GetCode("Tencent") ?? source.Id;
        var dest = destination.GetCode("Tencent") ?? destination.Id;
        
        var payload = JsonConvert.SerializeObject(new Payload
        {
            SourceText = text,
            Source = src,
            Target = dest,
            ProjectId = 0
        });

        var headers = BuildHeaders(
            "tmt", Endpoint, "ap-guangzhou", "TextTranslate", "2018-03-21", DateTime.Now, payload);

        var request = new RestRequest($"https://{Endpoint}", Method.Post)
            .AddHeader("Content-Type", "application/json")
            .AddBody(payload);

        foreach (var kv in headers) request.AddHeader(kv.Key, kv.Value);

        request.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);
        var response = await _client.ExecuteAsync(request, cancellationToken);

        if (showOriginal) return response.Content ?? Resources.RequestError;

        if (response.Content == null)
        {
            _logger.LogWarning("Translation failed: null response");
            return Resources.RequestError;
        }
        
        var tencent = JObject.Parse(response.Content);
        
        if (tencent["Response"]?["Error"] != null)
        {
            _logger.LogWarning("API error: {Response}", response.Content);
            return Resources.RequestError;
        }
        
        var result = tencent["Response"]!["TargetText"]!.ToString();
        _logger.LogDebug("Translation completed: ResultLength={Length}", result.Length);
        return result;
    }

    private static string Sha256Hex(string s)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexStringLower(hashBytes);
    }

    private static byte[] HmacSha256(byte[] key, byte[] msg)
    {
        using var mac = new HMACSHA256(key);
        return mac.ComputeHash(msg);
    }

    private Dictionary<string, string> BuildHeaders(string service, string endpoint, string region,
        string action, string version, DateTime date, string requestPayload)
    {
        var dateStr = date.ToString("yyyy-MM-dd");
        var requestTimestamp = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();

        // Step 1. Create canonical request
        const string algorithm = "TC3-HMAC-SHA256";
        const string httpRequestMethod = "POST";
        const string canonicalUri = "/";
        const string canonicalQueryString = "";
        const string contentType = "application/json";
        var canonicalHeaders = "content-type:" + contentType + "; charset=utf-8\n" + "host:" + endpoint + "\n";
        const string signedHeaders = "content-type;host";
        var hashedRequestPayload = Sha256Hex(requestPayload);
        var canonicalRequest = httpRequestMethod + "\n"
                                                 + canonicalUri + "\n"
                                                 + canonicalQueryString + "\n"
                                                 + canonicalHeaders + "\n"
                                                 + signedHeaders + "\n"
                                                 + hashedRequestPayload;

        // Step 2. Create string to sign
        var credentialScope = dateStr + "/" + service + "/" + "tc3_request";
        var hashedCanonicalRequest = Sha256Hex(canonicalRequest);
        var stringToSign = algorithm + "\n" + requestTimestamp + "\n" + credentialScope + "\n" +
                           hashedCanonicalRequest;

        // Step 3. Calculate signature
        var tc3SecretKey = Encoding.UTF8.GetBytes("TC3" + _secretKey);
        var secretDate = HmacSha256(tc3SecretKey, Encoding.UTF8.GetBytes(dateStr));
        var secretService = HmacSha256(secretDate, Encoding.UTF8.GetBytes(service));
        var secretSigning = HmacSha256(secretService, Encoding.UTF8.GetBytes("tc3_request"));
        var signatureBytes = HmacSha256(secretSigning, Encoding.UTF8.GetBytes(stringToSign));
        var signature = Convert.ToHexStringLower(signatureBytes);

        // Step 4. Create authorization header
        var authorization = algorithm + " "
                                      + "Credential=" + _secretId + "/" + credentialScope + ", "
                                      + "SignedHeaders=" + signedHeaders + ", "
                                      + "Signature=" + signature;

        return new Dictionary<string, string>
        {
            { "Authorization", authorization },
            { "Host", endpoint },
            { "Content-Type", contentType + "; charset=utf-8" },
            { "X-TC-Timestamp", requestTimestamp.ToString() },
            { "X-TC-Version", version },
            { "X-TC-Action", action },
            { "X-TC-Region", region }
        };
    }

    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
    private class Payload
    {
        public string? SourceText { get; set; }
        public string? Source { get; set; }
        public string? Target { get; set; }
        public int ProjectId { get; set; }
    }
}