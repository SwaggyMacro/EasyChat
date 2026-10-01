using EasyChat.Contracts.Platform;
using EasyChat.Shared.Results;

namespace EasyChat.Infrastructure.MacOS.Input;

/// <summary>
/// Image access to the macOS general pasteboard. The frame is encoded as PNG with the system codec,
/// so the pasteboard carries a representation every macOS application can paste, rather than a raw
/// buffer only EasyChat understands.
/// </summary>
internal sealed class MacClipboardImage(MacPasteboard pasteboard) : IClipboardImage
{
    public ValueTask<Result> WriteAsync(
        ImageFrame image,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.PixelFormat != ImagePixelFormat.Bgra32)
        {
            return ValueTask.FromResult(Result.Failure(new Error(
                "clipboard.image-format-unsupported",
                $"The macOS pasteboard writer only accepts BGRA32 frames, not {image.PixelFormat}.")));
        }

        return pasteboard.WriteAsync(
            "clipboard.image-write-failed",
            () => pasteboard.WritePng(image),
            cancellationToken);
    }
}
