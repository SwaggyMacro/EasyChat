using System.Globalization;
using System.Security.Cryptography;
using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.MacOS.Input;

/// <summary>A decoded macOS interaction target: an application, and optionally one of its windows.</summary>
internal readonly record struct MacTarget(int ProcessIdentifier, long WindowNumber)
{
    /// <summary>No window was resolved, so the target is the application's frontmost window.</summary>
    internal bool IsApplicationWide => WindowNumber == 0;
}

/// <summary>
/// Encodes and decodes <see cref="ExternalTargetToken"/> for macOS.
/// </summary>
/// <remarks>
/// The encoding is <c>mac:&lt;session&gt;:&lt;pid&gt;:&lt;window&gt;</c> and is private to this
/// assembly; Application and Presentation only ever compare and pass the string along. The session
/// segment is a random value generated once per process, which is what makes a token useless outside
/// the run that issued it: a persisted token, or one minted by another platform, fails to decode
/// instead of resolving to whatever process happens to hold that id now.
/// </remarks>
internal static class MacTargetTokens
{
    private const string Prefix = "mac:";

    private static readonly string Session =
        Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

    internal static ExternalTargetToken FromTarget(MacTarget target) =>
        target.ProcessIdentifier <= 0
            ? ExternalTargetToken.None
            : new ExternalTargetToken(
                $"{Prefix}{Session}:{target.ProcessIdentifier}:{target.WindowNumber}");

    internal static bool TryDecode(ExternalTargetToken token, out MacTarget target)
    {
        target = default;
        if (token.IsEmpty || !token.Value.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var segments = token.Value.Split(':');
        if (segments.Length != 4
            || !string.Equals(segments[1], Session, StringComparison.Ordinal)
            || !int.TryParse(segments[2], CultureInfo.InvariantCulture, out var processIdentifier)
            || !long.TryParse(segments[3], CultureInfo.InvariantCulture, out var window)
            || processIdentifier <= 0)
        {
            return false;
        }

        target = new MacTarget(processIdentifier, window);
        return true;
    }
}
