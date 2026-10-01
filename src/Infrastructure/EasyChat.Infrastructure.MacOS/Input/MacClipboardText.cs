using EasyChat.Contracts.Platform;
using EasyChat.Shared.Results;

namespace EasyChat.Infrastructure.MacOS.Input;

/// <summary>Plain-text access to the macOS general pasteboard.</summary>
internal sealed class MacClipboardText(MacPasteboard pasteboard) : IClipboardText
{
    public ValueTask<Result<string?>> ReadAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pasteboard);
        return pasteboard.ReadAsync(
            "clipboard.read-failed",
            pasteboard.ReadText,
            cancellationToken);
    }

    public ValueTask<Result> WriteAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return pasteboard.WriteAsync(
            "clipboard.write-failed",
            () => pasteboard.WriteText(text),
            cancellationToken);
    }
}
