using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EasyChat.Services.Speech.EdgeTts;

public class EdgeTtsService : IEdgeTtsService
{
    public async Task SynthesizeAsync(string text, string voice, string outputFile, string rate = "+0%", string volume = "+0%", string pitch = "+0Hz")
    {
        using var audioStream = await StreamAsync(text, voice, rate, volume, pitch);
        using var fileStream = new FileStream(outputFile, FileMode.Create, FileAccess.Write);
        await audioStream.CopyToAsync(fileStream);
    }

    public async Task<Stream> StreamAsync(string text, string voice, string rate = "+0%", string volume = "+0%", string pitch = "+0Hz")
    {
        var memoryStream = new MemoryStream();
        
        using var client = new ClientWebSocket();
        
        // Add headers
        // WebSocket headers are added during ConnectAsync usually via options, but ClientWebSocket support for custom headers is via Options.SetRequestHeader
        foreach (var header in EdgeTtsConstants.WssHeaders)
        {
            if (header.Key == "Sec-WebSocket-Version") continue;
            client.Options.SetRequestHeader(header.Key, header.Value);
        }
        
        // Add Cookie with MUID
        var muid = EdgeTtsTokenGenerator.GenerateMuid();
        client.Options.SetRequestHeader("Cookie", $"muid={muid};");

        var connectId = Guid.NewGuid().ToString("N");
        var secMsGec = EdgeTtsTokenGenerator.GenerateSecMsGec();

        // We need to append query parameters manually or securely
        // The WssUrl in constants already has TrustedClientToken, we need to append others.
        // WssUrl: wss://.../edge/v1?TrustedClientToken=...
        var uri = new Uri($"{EdgeTtsConstants.WssUrl}&ConnectionId={connectId}&Sec-MS-GEC={secMsGec}&Sec-MS-GEC-Version={EdgeTtsConstants.SecMsGecVersion}");

        await client.ConnectAsync(uri, CancellationToken.None);

        // 1. Send Command Request
        await SendCommandRequestAsync(client);

        // 2. Send SSML Request
        var requestId = Guid.NewGuid().ToString("N");
        var ssml = MakeSsml(text, voice, rate, volume, pitch);
        await SendSsmlRequestAsync(client, requestId, ssml);

        // 3. Receive Loop
        var buffer = new byte[1024 * 16];
        var receiveSegment = new ArraySegment<byte>(buffer);

        while (client.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            var messageBuffer = new List<byte>();
            
            do
            {
                result = await client.ReceiveAsync(receiveSegment, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await client.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                    return memoryStream;
                }
                
                messageBuffer.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var textMsg = Encoding.UTF8.GetString(messageBuffer.ToArray());
                if (textMsg.Contains("Path:turn.end"))
                {
                    // End of turn, successful completion
                    break;
                }
            }
            else if (result.MessageType == WebSocketMessageType.Binary)
            {
                var data = messageBuffer.ToArray();
                if (data.Length < 2) continue;

                var headerLength = (data[0] << 8) | data[1];
                if (data.Length < headerLength + 2) continue;

                var headersText = Encoding.UTF8.GetString(data, 2, headerLength);
                
                // Check if it's audio
                if (headersText.Contains("Path:audio"))
                {
                    // The rest is audio data
                    // There might be some logic needed to verify Content-Type, typically audio/mpeg
                    var audioData = new ReadOnlyMemory<byte>(data, headerLength + 2, data.Length - (headerLength + 2));
                    await memoryStream.WriteAsync(audioData);
                }
            }
        }
        
        memoryStream.Position = 0;
        return memoryStream;
    }

    private async Task SendCommandRequestAsync(ClientWebSocket client)
    {
        var timestamp = GetJsDateString();
        var command = $"X-Timestamp:{timestamp}\r\n" +
                      "Content-Type:application/json; charset=utf-8\r\n" +
                      "Path:speech.config\r\n\r\n" +
                      "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{" +
                      "\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"" +
                      "}," +
                      "\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"" +
                      "}}}}\r\n";
        
        var bytes = Encoding.UTF8.GetBytes(command);
        await client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task SendSsmlRequestAsync(ClientWebSocket client, string requestId, string ssml)
    {
        var timestamp = GetJsDateString();
        var request = $"X-RequestId:{requestId}\r\n" +
                      "Content-Type:application/ssml+xml\r\n" +
                      $"X-Timestamp:{timestamp}Z\r\n" +
                      "Path:ssml\r\n\r\n" +
                      $"{ssml}";
        
        var bytes = Encoding.UTF8.GetBytes(request);
        await client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private string MakeSsml(string text, string voice, string rate, string volume, string pitch)
    {
        // Simple XML escaping
        var escapedText = System.Security.SecurityElement.Escape(text);
        
        return $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>" +
               $"<voice name='{voice}'>" +
               $"<prosody pitch='{pitch}' rate='{rate}' volume='{volume}'>" +
               $"{escapedText}" +
               $"</prosody>" +
               $"</voice>" +
               $"</speak>";
    }

    private string GetJsDateString()
    {
        // Format: Fri Aug 06 2021 15:00:00 GMT+0000 (Coordinated Universal Time)
        return DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'", CultureInfo.InvariantCulture);
    }
}
