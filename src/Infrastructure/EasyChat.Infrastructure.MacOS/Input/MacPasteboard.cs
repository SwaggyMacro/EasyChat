using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Native;
using EasyChat.Shared.Results;

namespace EasyChat.Infrastructure.MacOS.Input;

/// <summary>
/// The single owner of EasyChat's access to the macOS general pasteboard.
/// </summary>
/// <remarks>
/// All three clipboard ports share this instance so that a capture, a temporary write and a restore
/// can never interleave: selection capture writes the pasteboard and puts it back, and a concurrent
/// read would otherwise observe, or overwrite, the temporary value. Every operation also opens its
/// own autorelease pool, because the AppKit calls answer autoreleased objects.
/// </remarks>
internal sealed class MacPasteboard
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal async ValueTask<Result<T>> ReadAsync<T>(
        string errorCode,
        Func<T> operation,
        CancellationToken cancellationToken)
    {
        EnsureSupportedHost();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var pool = AutoreleasePool.Push();
            return Result<T>.Success(operation());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result<T>.Failure(new Error(errorCode, exception.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async ValueTask<Result> WriteAsync(
        string errorCode,
        Func<bool> operation,
        CancellationToken cancellationToken)
    {
        EnsureSupportedHost();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var pool = AutoreleasePool.Push();
            return operation()
                ? Result.Success()
                : Result.Failure(new Error(
                    errorCode,
                    "The macOS pasteboard rejected the write."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Result.Failure(new Error(errorCode, exception.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    internal long ChangeCount() => PasteboardNative.ChangeCount();

    internal string? ReadText() => PasteboardNative.ReadText();

    internal bool WriteText(string text) => PasteboardNative.WriteText(text);

    internal bool WritePng(ImageFrame image) =>
        PasteboardNative.WriteData(
            ImageEncodingNative.EncodePng(
                image.Pixels.Span,
                image.Width,
                image.Height,
                image.Stride),
            PasteboardType.Png);

    /// <summary>
    /// Copies every representation of every pasteboard item. A type whose lazy provider declines to
    /// hand over data is reported rather than dropped, so a restore never claims to be complete when
    /// it is not.
    /// </summary>
    internal MacPasteboardContents Capture()
    {
        var changeCount = PasteboardNative.ChangeCount();
        var items = PasteboardNative.ReadItems(out var unreadable);
        return new MacPasteboardContents(changeCount, items, unreadable);
    }

    internal bool Restore(MacPasteboardContents contents) =>
        PasteboardNative.WriteItems(contents.Items);

    private static void EnsureSupportedHost()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
            throw new PlatformNotSupportedException(
                "The macOS pasteboard requires macOS 26 or later.");
    }
}

/// <summary>
/// A captured pasteboard, with the types macOS refused to serialise recorded alongside it.
/// </summary>
internal sealed record MacPasteboardContents(
    long ChangeCount,
    IReadOnlyList<PasteboardItemData> Items,
    IReadOnlyList<string> UnreadableTypes);
