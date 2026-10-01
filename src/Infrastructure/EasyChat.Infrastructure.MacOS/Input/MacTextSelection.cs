using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Native;
using EasyChat.Shared.Results;

namespace EasyChat.Infrastructure.MacOS.Input;

/// <summary>
/// Selects the whole value of the focused text element through the accessibility API.
/// </summary>
/// <remarks>
/// Setting the selected range directly is preferred over sending Command+A because it reports the
/// range that actually resulted, which is how the caller tells a real select-all from a control that
/// ignored the request. When there is no focused text element — including when Accessibility has not
/// been approved — this answers "no focused control" so the caller falls back to the key command,
/// exactly as it does on Windows for a window that is not an edit control.
/// </remarks>
internal sealed class MacTextSelection : ITextSelection
{
    public ValueTask<Result<TextSelectionRange>> SelectAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            return ValueTask.FromResult(Result<TextSelectionRange>.Success(
                new TextSelectionRange(false, 0, 0)));
        }

        var element = MacAccessibilityText.CopyFocusedElement();
        if (element == IntPtr.Zero)
        {
            return ValueTask.FromResult(Result<TextSelectionRange>.Success(
                new TextSelectionRange(false, 0, 0)));
        }

        try
        {
            return ValueTask.FromResult(SelectAll(element));
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(Result<TextSelectionRange>.Failure(
                new Error("selection.select-all-failed", exception.Message)));
        }
        finally
        {
            CoreFoundationNative.CFRelease(element);
        }
    }

    private static Result<TextSelectionRange> SelectAll(IntPtr element)
    {
        if (!MacAccessibilityText.TryReadLength(element, out var length))
            return Result<TextSelectionRange>.Success(new TextSelectionRange(false, 0, 0));

        var range = AccessibilityNative.CreateRange(
            new CoreFoundationRange { Location = 0, Length = length });
        if (range == IntPtr.Zero)
        {
            return Result<TextSelectionRange>.Failure(new Error(
                "selection.select-all-failed",
                "The accessibility range could not be created."));
        }

        try
        {
            if (!AccessibilityNative.SetAttribute(
                    element,
                    AccessibilityNative.SelectedTextRangeAttribute,
                    range))
            {
                // The element exposes text but refuses a programmatic selection, so the caller has
                // to fall back to a keystroke rather than believe the selection happened.
                return Result<TextSelectionRange>.Success(new TextSelectionRange(false, 0, 0));
            }
        }
        finally
        {
            CoreFoundationNative.CFRelease(range);
        }

        // Read the range back: an element may clamp or ignore the request, and only the resulting
        // range tells the caller whether the whole value is really selected.
        return AccessibilityNative.TryReadRange(
            element,
            AccessibilityNative.SelectedTextRangeAttribute,
            out var applied)
            ? Result<TextSelectionRange>.Success(new TextSelectionRange(
                true,
                (int)applied.Location,
                (int)(applied.Location + applied.Length)))
            : Result<TextSelectionRange>.Success(new TextSelectionRange(true, 0, length));
    }
}
