using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Native;
using EasyChat.Shared.Results;
using Microsoft.Extensions.Logging;

namespace EasyChat.Infrastructure.MacOS.Input;

/// <summary>
/// Reads whatever the user has selected in the application they are working in.
/// </summary>
/// <remarks>
/// The accessibility API is tried first because it reads the selection without touching anything:
/// no synthetic keystroke, no pasteboard write, nothing the user can notice. Only when that fails —
/// an application that exposes no accessible text, or Accessibility not approved — does the capture
/// fall back to a synthesised Command+C, and that path saves the pasteboard first and puts it back
/// only if nothing else wrote to it in the meantime.
/// </remarks>
internal sealed class MacSelectedTextCapture(
    IClipboardSnapshots clipboardSnapshots,
    IClipboardText clipboardText,
    IPointerPosition pointerPosition,
    IKeyboardState keyboardState,
    ITextSelection textSelection,
    ITextDelivery textDelivery,
    ILogger<MacSelectedTextCapture> logger) : ISelectedTextCapture
{
    private static readonly TimeSpan CopySettleDelay = TimeSpan.FromMilliseconds(10);
    private const int CopyPollAttempts = 20;

    public async ValueTask<Result<SelectedText>> CaptureAsync(
        SelectionCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            return Result<SelectedText>.Failure(new Error(
                "selection.unsupported",
                "Selection capture requires macOS 26 or later."));
        }

        if (!TryDecode(request.ExpectedForegroundTarget, out var expectedForeground)
            || !TryDecode(request.ExpectedFocusedTarget, out var expectedFocused))
        {
            return Result<SelectedText>.Failure(new Error(
                "selection.target-invalid",
                "The expected target token was not issued by this macOS session."));
        }

        if (!HasExpectedContext(expectedForeground))
            return ContextChanged();

        if (request.CaptureAll)
        {
            var selected = await textSelection.SelectAllAsync(cancellationToken)
                .ConfigureAwait(false);
            if (selected.IsFailure || !IsCompleteSelection(selected.Value))
            {
                if (HasPressedCopyKey())
                    return KeyboardBusy();

                var command = await textDelivery
                    .SendCommandAsync(StandardTextCommand.SelectAll, cancellationToken)
                    .ConfigureAwait(false);
                if (command.IsFailure)
                    return Result<SelectedText>.Failure(command.Error);
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        var position = request.PointerPosition ?? pointerPosition.GetCurrent();
        var source = SourceToken(expectedForeground);

        if (!request.CopyOnly)
        {
            var direct = ReadWithAccessibility();
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return Result<SelectedText>.Success(new SelectedText(
                    direct,
                    source,
                    "AXSelectedText",
                    position));
            }
        }

        return request.DirectOnly
            ? EmptySelection()
            : await CopyThroughPasteboardAsync(
                request,
                source,
                position,
                expectedForeground,
                cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Result<SelectedText>> CopyThroughPasteboardAsync(
        SelectionCaptureRequest request,
        ExternalTargetToken source,
        PhysicalScreenPoint position,
        MacTarget? expectedForeground,
        CancellationToken cancellationToken)
    {
        IClipboardSnapshot? snapshot = null;
        IClipboardChangeToken? restoreToken = null;
        try
        {
            if (request.PreserveClipboard)
            {
                var captured = await clipboardSnapshots.CaptureAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (captured.IsFailure)
                    return Result<SelectedText>.Failure(captured.Error);
                snapshot = captured.Value;
            }

            // Injecting Command+C while the user still holds their own modifiers would produce a
            // combination neither of us asked for, so the capture stands down instead.
            if (HasPressedCopyKey())
                return KeyboardBusy();
            if (!HasExpectedContext(expectedForeground))
                return ContextChanged();

            var before = await clipboardSnapshots.GetChangeTokenAsync(cancellationToken)
                .ConfigureAwait(false);
            var copy = await textDelivery
                .SendCommandAsync(StandardTextCommand.Copy, cancellationToken)
                .ConfigureAwait(false);
            if (copy.IsFailure)
                return Result<SelectedText>.Failure(copy.Error);

            var text = await WaitForCopiedTextAsync(before, cancellationToken).ConfigureAwait(false);
            if (snapshot is not null)
            {
                var token = await clipboardSnapshots.GetChangeTokenAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (token.IsSuccess)
                    restoreToken = token.Value;
            }

            return string.IsNullOrWhiteSpace(text)
                ? EmptySelection()
                : Result<SelectedText>.Success(new SelectedText(text, source, "Command+C", position));
        }
        finally
        {
            if (snapshot is not null)
            {
                var restored = restoreToken is not null
                    ? await clipboardSnapshots.RestoreIfUnchangedAsync(
                        snapshot,
                        restoreToken,
                        CancellationToken.None).ConfigureAwait(false)
                    : await clipboardSnapshots.RestoreAsync(snapshot, CancellationToken.None)
                        .ConfigureAwait(false);
                if (restored.IsFailure)
                {
                    logger.LogWarning(
                        "Unable to restore the pasteboard after selection capture: {Error}",
                        restored.Error.Message);
                }

                await snapshot.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Waits for the copy to land. The pasteboard's change count is what says the target responded,
    /// which is more reliable than polling for non-empty text: the user may well have had the same
    /// string on the pasteboard already.
    /// </summary>
    private async ValueTask<string?> WaitForCopiedTextAsync(
        Result<IClipboardChangeToken> before,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < CopyPollAttempts; attempt++)
        {
            await Task.Delay(CopySettleDelay, cancellationToken).ConfigureAwait(false);
            if (before.IsSuccess)
            {
                var current = await clipboardSnapshots
                    .IsChangeTokenCurrentAsync(before.Value, cancellationToken)
                    .ConfigureAwait(false);
                if (current.IsSuccess && current.Value)
                    continue;
            }

            var read = await clipboardText.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (read.IsSuccess && !string.IsNullOrEmpty(read.Value))
                return read.Value;
        }

        return null;
    }

    private static string? ReadWithAccessibility()
    {
        var element = MacAccessibilityText.CopyFocusedElement();
        if (element == IntPtr.Zero)
            return null;

        try
        {
            return MacAccessibilityText.ReadSelectedText(element);
        }
        finally
        {
            CoreFoundationNative.CFRelease(element);
        }
    }

    private static ExternalTargetToken SourceToken(MacTarget? expectedForeground)
    {
        if (expectedForeground is { } expected)
            return MacTargetTokens.FromTarget(expected);

        using var pool = AutoreleasePool.Push();
        return MacTargetTokens.FromTarget(new MacTarget(
            WorkspaceNative.ProcessIdentifier(WorkspaceNative.FrontmostApplication()),
            0));
    }

    /// <summary>
    /// macOS activates per application, so "the context is unchanged" means the same application is
    /// still frontmost. There is no separate focused-window identity to compare.
    /// </summary>
    private static bool HasExpectedContext(MacTarget? expectedForeground)
    {
        if (expectedForeground is not { } expected)
            return true;

        using var pool = AutoreleasePool.Push();
        return WorkspaceNative.ProcessIdentifier(WorkspaceNative.FrontmostApplication())
               == expected.ProcessIdentifier;
    }

    private bool HasPressedCopyKey() =>
        keyboardState.IsPressed(KeyboardKey.Control)
        || keyboardState.IsPressed(KeyboardKey.Alt)
        || keyboardState.IsPressed(KeyboardKey.Shift)
        || keyboardState.IsPressed(KeyboardKey.LeftMeta)
        || keyboardState.IsPressed(KeyboardKey.RightMeta)
        || keyboardState.IsPressed(KeyboardKey.C);

    private static bool TryDecode(ExternalTargetToken token, out MacTarget? target)
    {
        if (token.IsEmpty)
        {
            target = null;
            return true;
        }

        if (MacTargetTokens.TryDecode(token, out var decoded))
        {
            target = decoded;
            return true;
        }

        target = null;
        return false;
    }

    private static bool IsCompleteSelection(TextSelectionRange selection) =>
        selection.HasFocusedControl && selection.Start == 0 && selection.End > selection.Start;

    private static Result<SelectedText> ContextChanged() => Result<SelectedText>.Failure(
        new Error(
            "selection.context-changed",
            "The source application changed before text could be captured."));

    private static Result<SelectedText> EmptySelection() => Result<SelectedText>.Failure(
        new Error("selection.empty", "No selected text was available."));

    private static Result<SelectedText> KeyboardBusy() => Result<SelectedText>.Failure(
        new Error(
            "selection.keyboard-busy",
            "Selection capture cannot inject input while shortcut keys are pressed."));
}
