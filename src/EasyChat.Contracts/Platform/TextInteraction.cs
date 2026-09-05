using EasyChat.Shared.Results;

namespace EasyChat.Contracts.Platform;

public sealed record SelectionCaptureRequest(
    PhysicalScreenPoint? PointerPosition = null,
    ExternalTargetToken ExpectedForegroundTarget = default,
    ExternalTargetToken ExpectedFocusedTarget = default,
    bool CopyOnly = false,
    bool CaptureAll = false,
    bool DirectOnly = false,
    bool PreserveClipboard = true);

public sealed record SelectedText(
    string Text,
    ExternalTargetToken SourceTarget,
    string CaptureMethod,
    PhysicalScreenPoint? PointerPosition = null);

public interface ISelectedTextCapture
{
    ValueTask<Result<SelectedText>> CaptureAsync(
        SelectionCaptureRequest request,
        CancellationToken cancellationToken = default);
}

public enum TextDeliveryMode
{
    Type,
    Paste,
    Message
}

public enum StandardTextCommand
{
    SelectAll,
    Delete,
    Copy,
    Paste
}

public sealed record TextDeliveryRequest(
    string Text,
    ExternalTargetToken Target,
    TextDeliveryMode Mode,
    TimeSpan KeyDelay);

public interface ITextDelivery
{
    ValueTask<Result> DeliverAsync(
        TextDeliveryRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<Result> SendCommandAsync(
        StandardTextCommand command,
        CancellationToken cancellationToken = default);

    ValueTask<Result> SendKeyCombinationAsync(
        string combination,
        CancellationToken cancellationToken = default);
}

public readonly record struct TextSelectionRange(bool HasFocusedControl, int Start, int End);

public interface ITextSelection
{
    ValueTask<Result<TextSelectionRange>> SelectAllAsync(
        CancellationToken cancellationToken = default);
}
