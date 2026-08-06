using EasyChat.Shared.Results;

namespace EasyChat.Contracts.Platform;

public interface IClipboardSnapshot : IAsyncDisposable;

public interface IClipboardChangeToken;

public interface IClipboardSnapshots
{
    ValueTask<Result<IClipboardChangeToken>> GetChangeTokenAsync(
        CancellationToken cancellationToken = default);

    ValueTask<Result<bool>> IsChangeTokenCurrentAsync(
        IClipboardChangeToken changeToken,
        CancellationToken cancellationToken = default);

    ValueTask<Result<IClipboardSnapshot>> CaptureAsync(CancellationToken cancellationToken = default);

    ValueTask<Result> RestoreAsync(
        IClipboardSnapshot snapshot,
        CancellationToken cancellationToken = default);

    ValueTask<Result> RestoreIfUnchangedAsync(
        IClipboardSnapshot snapshot,
        IClipboardChangeToken expectedChangeToken,
        CancellationToken cancellationToken = default);
}

public interface IClipboardText
{
    ValueTask<Result<string?>> ReadAsync(CancellationToken cancellationToken = default);

    ValueTask<Result> WriteAsync(string text, CancellationToken cancellationToken = default);
}

public interface IClipboardImage
{
    ValueTask<Result> WriteAsync(
        ImageFrame image,
        CancellationToken cancellationToken = default);
}
