using EasyChat.Contracts.Platform;
using EasyChat.Shared.Results;

namespace EasyChat.Infrastructure.MacOS.Input;

/// <summary>
/// Backs up and restores the macOS general pasteboard around EasyChat's temporary writes.
/// </summary>
/// <remarks>
/// The change token is the pasteboard's <c>changeCount</c>, which macOS increments on every write by
/// any process. A restore that is conditional on it therefore leaves alone anything the user copied
/// while EasyChat was working.
/// </remarks>
internal sealed class MacClipboardSnapshots(MacPasteboard pasteboard) : IClipboardSnapshots
{
    public ValueTask<Result<IClipboardChangeToken>> GetChangeTokenAsync(
        CancellationToken cancellationToken = default) =>
        pasteboard.ReadAsync<IClipboardChangeToken>(
            "clipboard.change-token-failed",
            () => new ChangeToken(pasteboard.ChangeCount()),
            cancellationToken);

    public ValueTask<Result<bool>> IsChangeTokenCurrentAsync(
        IClipboardChangeToken changeToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changeToken);
        if (changeToken is not ChangeToken token)
        {
            return ValueTask.FromResult(Result<bool>.Failure(new Error(
                "clipboard.change-token-invalid",
                "The clipboard change token was not created by this service.")));
        }

        return pasteboard.ReadAsync(
            "clipboard.change-token-failed",
            () => pasteboard.ChangeCount() == token.Value,
            cancellationToken);
    }

    public async ValueTask<Result<IClipboardSnapshot>> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var captured = await pasteboard.ReadAsync(
            "clipboard.capture-failed",
            pasteboard.Capture,
            cancellationToken).ConfigureAwait(false);
        if (captured.IsFailure)
            return Result<IClipboardSnapshot>.Failure(captured.Error);

        // A lazy provider that refuses to serialise its type cannot be restored later, and silently
        // losing it would hand the user back a clipboard that looks intact but is not.
        if (captured.Value.UnreadableTypes.Count > 0)
        {
            return Result<IClipboardSnapshot>.Failure(new Error(
                "clipboard.capture-incomplete",
                "The clipboard holds types macOS would not serialise: " +
                string.Join(", ", captured.Value.UnreadableTypes)));
        }

        return Result<IClipboardSnapshot>.Success(new Snapshot(captured.Value));
    }

    public ValueTask<Result> RestoreAsync(
        IClipboardSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot is Snapshot owned
            ? Restore(owned, expected: null, cancellationToken)
            : ValueTask.FromResult(Invalid());
    }

    public ValueTask<Result> RestoreIfUnchangedAsync(
        IClipboardSnapshot snapshot,
        IClipboardChangeToken expectedChangeToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(expectedChangeToken);
        return snapshot is Snapshot owned && expectedChangeToken is ChangeToken token
            ? Restore(owned, token.Value, cancellationToken)
            : ValueTask.FromResult(Invalid());
    }

    private ValueTask<Result> Restore(
        Snapshot snapshot,
        long? expected,
        CancellationToken cancellationToken) =>
        pasteboard.WriteAsync(
            "clipboard.restore-failed",
            () =>
            {
                var contents = snapshot.Take();
                if (contents is null)
                    return true;

                // Someone else wrote to the pasteboard after the snapshot, so the user's newer
                // content wins and the restore is skipped rather than overwriting it.
                return expected is { } value && pasteboard.ChangeCount() != value
                    || pasteboard.Restore(contents);
            },
            cancellationToken);

    private static Result Invalid() =>
        Result.Failure(new Error(
            "clipboard.snapshot-invalid",
            "The clipboard snapshot or change token was not created by this service."));

    private sealed record ChangeToken(long Value) : IClipboardChangeToken;

    private sealed class Snapshot(MacPasteboardContents contents) : IClipboardSnapshot
    {
        private readonly Lock _gate = new();
        private MacPasteboardContents? _contents = contents;

        /// <summary>Hands the contents over once; a second restore is a no-op, not a repeat write.</summary>
        internal MacPasteboardContents? Take()
        {
            lock (_gate)
            {
                var taken = _contents;
                _contents = null;
                return taken;
            }
        }

        public ValueTask DisposeAsync()
        {
            Take();
            return ValueTask.CompletedTask;
        }
    }
}
