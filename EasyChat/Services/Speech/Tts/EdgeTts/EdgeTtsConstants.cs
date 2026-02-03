using System.Collections.Generic;

namespace EasyChat.Services.Speech.Tts.EdgeTts;

public static class EdgeTtsConstants
{
    public const string BaseUrl = "speech.platform.bing.com/consumer/speech/synthesize/readaloud";
    public const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    
    public static readonly string WssUrl = $"wss://{BaseUrl}/edge/v1?TrustedClientToken={TrustedClientToken}";
    
    public const string ChromiumFullVersion = "143.0.3650.75";
    public const string ChromiumMajorVersion = "143";
    public static readonly string SecMsGecVersion = $"1-{ChromiumFullVersion}";

    public static readonly Dictionary<string, string> BaseHeaders = new()
    {
        { "User-Agent", $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{ChromiumMajorVersion}.0.0.0 Safari/537.36 Edg/{ChromiumMajorVersion}.0.0.0" },
        { "Accept-Encoding", "gzip, deflate, br, zstd" },
        { "Accept-Language", "en-US,en;q=0.9" }
    };
    
    public static readonly Dictionary<string, string> WssHeaders = new(BaseHeaders)
    {
        { "Pragma", "no-cache" },
        { "Cache-Control", "no-cache" },
        { "Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold" },
        { "Sec-WebSocket-Version", "13" }
    };
}
