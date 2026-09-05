using EasyChat.Contracts.Input;
using EasyChat.Contracts.Platform;
using EasyChat.Shared.Results;

namespace EasyChat.Application.Input;

public sealed class InputDeliveryUseCases : IInputDeliveryUseCases
{
    private readonly IWindowFocus _windowFocus;
    private readonly IPlatformAccessUseCases _platformAccess;
    private readonly ITextSelection _textSelection;
    private readonly ITextDelivery _textDelivery;
    private readonly IInputDeliveryDelay _delay;

    public InputDeliveryUseCases(
        IPlatformAccessUseCases platformAccess,
        IWindowFocus windowFocus,
        ITextSelection textSelection,
        ITextDelivery textDelivery)
        : this(platformAccess, windowFocus, textSelection, textDelivery, new SystemInputDeliveryDelay())
    {
    }

    internal InputDeliveryUseCases(
        IPlatformAccessUseCases platformAccess,
        IWindowFocus windowFocus,
        ITextSelection textSelection,
        ITextDelivery textDelivery,
        IInputDeliveryDelay delay)
    {
        _platformAccess = platformAccess ?? throw new ArgumentNullException(nameof(platformAccess));
        _windowFocus = windowFocus ?? throw new ArgumentNullException(nameof(windowFocus));
        _textSelection = textSelection ?? throw new ArgumentNullException(nameof(textSelection));
        _textDelivery = textDelivery ?? throw new ArgumentNullException(nameof(textDelivery));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public async ValueTask<Result> DeliverAsync(
        InputDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var access = await _platformAccess.EnsureAvailableAsync(
            PlatformCapability.TextDelivery,
            cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
            return Result.Failure(access.Error);

        var focused = await _windowFocus.EnsureFocusedAsync(request.Target, cancellationToken);
        if (focused.IsFailure)
            return focused;

        await _delay.DelayAsync(100, cancellationToken);

        if (request.ReplaceCurrentInput)
        {
            var selection = await _textSelection.SelectAllAsync(cancellationToken);
            if (selection.IsFailure || !IsCompleteSelection(selection.Value))
            {
                var selectAll = await _textDelivery.SendCommandAsync(
                    StandardTextCommand.SelectAll,
                    cancellationToken);
                if (selectAll.IsFailure)
                    return selectAll;
            }

            await _delay.DelayAsync(50, cancellationToken);
            var delete = await _textDelivery.SendCommandAsync(
                StandardTextCommand.Delete,
                cancellationToken);
            if (delete.IsFailure)
                return delete;
            await _delay.DelayAsync(50, cancellationToken);
        }
        else
        {
            var beforeKey = await SendConfiguredKeyAsync(
                request.BeforeKey,
                waitAfter: true,
                cancellationToken);
            if (beforeKey.IsFailure)
                return beforeKey;
        }

        var delivered = await _textDelivery.DeliverAsync(
            new TextDeliveryRequest(
                request.Text,
                request.Target,
                request.Mode,
                request.KeyDelay),
            cancellationToken);
        if (delivered.IsFailure)
            return delivered;

        return await SendConfiguredKeyAsync(
            request.AfterKey,
            waitAfter: false,
            cancellationToken);
    }

    private async ValueTask<Result> SendConfiguredKeyAsync(
        string? combination,
        bool waitAfter,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(combination))
            return Result.Success();

        var sent = await _textDelivery.SendKeyCombinationAsync(combination, cancellationToken);
        if (sent.IsFailure || !waitAfter)
            return sent;

        await _delay.DelayAsync(100, cancellationToken);
        return Result.Success();
    }

    private static bool IsCompleteSelection(TextSelectionRange selection) =>
        selection.HasFocusedControl && selection.Start == 0 && selection.End > selection.Start;
}

internal interface IInputDeliveryDelay
{
    Task DelayAsync(int milliseconds, CancellationToken cancellationToken);
}

internal sealed class SystemInputDeliveryDelay : IInputDeliveryDelay
{
    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) =>
        Task.Delay(milliseconds, cancellationToken);
}
