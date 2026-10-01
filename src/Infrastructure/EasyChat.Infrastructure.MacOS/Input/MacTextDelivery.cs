using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Native;
using EasyChat.Shared.Results;
using Microsoft.Extensions.Logging;

namespace EasyChat.Infrastructure.MacOS.Input;

/// <summary>
/// Writes text back into the application the user was working in.
/// </summary>
/// <remarks>
/// Synthetic events need Accessibility approval; without it macOS drops them without an error, which
/// is why the workflow checks the capability before calling here. A posted event also carries no
/// destination, so the Type and Paste modes cannot tell what receives them — that is precisely why
/// the Message mode, which does know its target, refuses password fields.
/// </remarks>
internal sealed class MacTextDelivery(
    IClipboardSnapshots clipboardSnapshots,
    IClipboardText clipboardText,
    ILogger<MacTextDelivery> logger) : ITextDelivery
{
    private static readonly TimeSpan ClipboardSettleDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan PasteSettleDelay = TimeSpan.FromMilliseconds(200);

    public async ValueTask<Result> DeliverAsync(
        TextDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            EnsureSupportedHost();
            var delay = request.KeyDelay > TimeSpan.Zero ? request.KeyDelay : TimeSpan.Zero;
            return request.Mode switch
            {
                TextDeliveryMode.Paste => await PasteAsync(request.Text, cancellationToken)
                    .ConfigureAwait(false),
                TextDeliveryMode.Type => await TypeAsync(request.Text, delay, cancellationToken)
                    .ConfigureAwait(false),
                TextDeliveryMode.Message => SetSelectedText(request.Text),
                _ => Result.Failure(new Error(
                    "text-delivery.mode-unsupported",
                    $"Unsupported text delivery mode: {request.Mode}."))
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Text delivery failed.");
            return Result.Failure(new Error("text-delivery.failed", exception.Message));
        }
    }

    public ValueTask<Result> SendCommandAsync(
        StandardTextCommand command,
        CancellationToken cancellationToken = default) =>
        SendKeyCombinationAsync(MacKeyCombination.ForCommand(command), cancellationToken);

    public ValueTask<Result> SendKeyCombinationAsync(
        string combination,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(combination);
        cancellationToken.ThrowIfCancellationRequested();
        if (!MacKeyCombination.TryParse(combination, out var keystroke))
        {
            return ValueTask.FromResult(Result.Failure(new Error(
                "text-delivery.key-combination-invalid",
                $"'{combination}' is not a single macOS keystroke.")));
        }

        try
        {
            EnsureSupportedHost();
            CoreGraphicsEventNative.SendKey(keystroke.VirtualKey, keystroke.Modifiers);
            return ValueTask.FromResult(Result.Success());
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(Result.Failure(new Error(
                "text-delivery.key-combination-failed",
                exception.Message)));
        }
    }

    private async ValueTask<Result> TypeAsync(
        string text,
        TimeSpan keyDelay,
        CancellationToken cancellationToken)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rune.Value == '\r')
                continue;

            if (rune.Value == '\n')
                CoreGraphicsEventNative.SendKey(ReturnKey, EventModifiers.None);
            else
                CoreGraphicsEventNative.SendText(rune.ToString());

            if (keyDelay > TimeSpan.Zero)
                await Task.Delay(keyDelay, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }

    private async ValueTask<Result> PasteAsync(string text, CancellationToken cancellationToken)
    {
        IClipboardSnapshot? snapshot = null;
        try
        {
            var captured = await clipboardSnapshots.CaptureAsync(cancellationToken)
                .ConfigureAwait(false);
            if (captured.IsSuccess)
                snapshot = captured.Value;

            var written = await clipboardText.WriteAsync(text, cancellationToken)
                .ConfigureAwait(false);
            if (written.IsFailure)
                return written;

            await Task.Delay(ClipboardSettleDelay, cancellationToken).ConfigureAwait(false);
            var pasted = await SendCommandAsync(StandardTextCommand.Paste, cancellationToken)
                .ConfigureAwait(false);
            if (pasted.IsFailure)
                return pasted;

            // The paste has to land before the clipboard goes back, or the target reads the
            // restored content instead of the translation.
            await Task.Delay(PasteSettleDelay, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        finally
        {
            if (snapshot is not null)
            {
                var restored = await clipboardSnapshots
                    .RestoreAsync(snapshot, CancellationToken.None)
                    .ConfigureAwait(false);
                if (restored.IsFailure)
                {
                    logger.LogWarning(
                        "Unable to restore the clipboard after pasting: {Error}",
                        restored.Error.Message);
                }

                await snapshot.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Replaces the focused element's selection through the accessibility API. This is the only mode
    /// that knows its destination, so it is the only one that can refuse a password field, and it
    /// reports an element that does not support the attribute rather than claiming to have written.
    /// </summary>
    private static Result SetSelectedText(string text)
    {
        var element = MacAccessibilityText.CopyFocusedElement();
        if (element == IntPtr.Zero)
        {
            return Result.Failure(new Error(
                "text-delivery.no-focused-element",
                "No focused text element is available, or Accessibility access is missing."));
        }

        try
        {
            if (MacAccessibilityText.IsSecureField(element))
            {
                return Result.Failure(new Error(
                    "text-delivery.secure-field",
                    "EasyChat does not write into password fields."));
            }

            var value = FoundationNative.CreateString(text);
            return AccessibilityNative.SetAttribute(
                element,
                AccessibilityNative.SelectedTextAttribute,
                value)
                ? Result.Success()
                : Result.Failure(new Error(
                    "text-delivery.message-unsupported",
                    "The focused element does not accept a programmatic text replacement."));
        }
        finally
        {
            CoreFoundationNative.CFRelease(element);
        }
    }

    /// <summary>Value of <c>kVK_Return</c>.</summary>
    private const ushort ReturnKey = 36;

    private static void EnsureSupportedHost()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
            throw new PlatformNotSupportedException("Text delivery requires macOS 26 or later.");
    }
}
