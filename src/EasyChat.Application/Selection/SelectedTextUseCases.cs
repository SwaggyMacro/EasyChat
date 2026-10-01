using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Selection;
using EasyChat.Shared.Results;

namespace EasyChat.Application.Selection;

public sealed class SelectedTextUseCases : ISelectedTextUseCases
{
    private static readonly KeyboardKey[] CaptureKeys =
    [
        KeyboardKey.Control,
        KeyboardKey.Alt,
        KeyboardKey.Shift,
        KeyboardKey.LeftMeta,
        KeyboardKey.RightMeta,
        KeyboardKey.C
    ];

    private readonly ISelectedTextCapture _capture;
    private readonly IPlatformAccessUseCases _platformAccess;
    private readonly ITextSelection _textSelection;
    private readonly ITextDelivery _textDelivery;
    private readonly IKeyboardState _keyboardState;
    private readonly ISelectionDelay _delay;

    public SelectedTextUseCases(
        IPlatformAccessUseCases platformAccess,
        ISelectedTextCapture capture,
        ITextSelection textSelection,
        ITextDelivery textDelivery,
        IKeyboardState keyboardState)
        : this(platformAccess, capture, textSelection, textDelivery, keyboardState, new SystemSelectionDelay())
    {
    }

    internal SelectedTextUseCases(
        IPlatformAccessUseCases platformAccess,
        ISelectedTextCapture capture,
        ITextSelection textSelection,
        ITextDelivery textDelivery,
        IKeyboardState keyboardState,
        ISelectionDelay delay)
    {
        _platformAccess = platformAccess ?? throw new ArgumentNullException(nameof(platformAccess));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _textSelection = textSelection ?? throw new ArgumentNullException(nameof(textSelection));
        _textDelivery = textDelivery ?? throw new ArgumentNullException(nameof(textDelivery));
        _keyboardState = keyboardState ?? throw new ArgumentNullException(nameof(keyboardState));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public async ValueTask<Result<SelectedText>> CaptureAsync(
        SelectedTextCaptureCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var access = await _platformAccess.EnsureAvailableAsync(
            PlatformCapability.SelectedTextCapture,
            cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
            return Result<SelectedText>.Failure(access.Error);

        if (!await WaitForCaptureKeysReleasedAsync(cancellationToken).ConfigureAwait(false))
        {
            return Result<SelectedText>.Failure(new Error(
                "selection.modifier-timeout",
                "Selection capture was cancelled because shortcut keys were not released."));
        }

        if (command.Mode == SelectedTextCaptureMode.All)
        {
            var selected = await _textSelection.SelectAllAsync(cancellationToken).ConfigureAwait(false);
            if (selected.IsFailure || !IsCompleteSelection(selected.Value))
            {
                var fallback = await _textDelivery.SendCommandAsync(
                    StandardTextCommand.SelectAll,
                    cancellationToken).ConfigureAwait(false);
                if (fallback.IsFailure)
                    return Result<SelectedText>.Failure(fallback.Error);
            }

            await _delay.WaitAsync(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }

        return await _capture.CaptureAsync(
            new SelectionCaptureRequest(
                command.PointerPosition,
                command.ExpectedForegroundTarget,
                command.ExpectedFocusedTarget,
                CopyOnly: command.Mode is SelectedTextCaptureMode.Copy or SelectedTextCaptureMode.All,
                CaptureAll: false,
                DirectOnly: false,
                PreserveClipboard: true),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> WaitForCaptureKeysReleasedAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CaptureKeys.All(key => !_keyboardState.IsPressed(key)))
                return true;
            await _delay.WaitAsync(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static bool IsCompleteSelection(TextSelectionRange selection) =>
        selection.HasFocusedControl && selection.Start == 0 && selection.End > selection.Start;
}
