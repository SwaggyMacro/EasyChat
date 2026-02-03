using System;
using System.Security.Cryptography;
using System.Text;

namespace EasyChat.Services.Speech.Tts.EdgeTts;

public static class EdgeTtsTokenGenerator
{
    private const long WinEpoch = 11644473600;

    public static string GenerateSecMsGec()
    {
        // Get the current timestamp in Unix format (seconds)
        double ticks = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        // Switch to Windows file time epoch (1601-01-01 00:00:00 UTC)
        // Note: Python code added WIN_EPOCH (11644473600) to unix timestamp.
        // Windows File Time starts at 1601-01-01. Unix Epoch is 1970-01-01.
        // Difference is 11,644,473,600 seconds.
        ticks += WinEpoch;

        // Round down to the nearest 5 minutes (300 seconds)
        ticks -= ticks % 300;

        // Convert the ticks to 100-nanosecond intervals (Windows file time format)
        // 1 second = 10,000,000 ticks (100-nanosecond intervals)
        // Python code: ticks *= S_TO_NS / 100
        // S_TO_NS is 1e9 (1,000,000,000). 
        // 1e9 / 100 = 10,000,000.
        ticks *= 10_000_000;

        // Create the string to hash
        string strToHash = $"{ticks:F0}{EdgeTtsConstants.TrustedClientToken}";
        
        // Compute SHA256 hash
        using var sha256 = SHA256.Create();
        byte[] bytes = Encoding.ASCII.GetBytes(strToHash);
        byte[] hashBytes = sha256.ComputeHash(bytes);
        
        return Convert.ToHexString(hashBytes).ToUpper();
    }

    public static string GenerateMuid()
    {
        return Guid.NewGuid().ToString("N").ToUpper();
    }
}
