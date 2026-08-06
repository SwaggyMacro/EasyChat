using EasyChat.Contracts.Platform;
using EasyChat.Shared.Results;

namespace EasyChat.Contracts.Selection;

public enum SelectedTextCaptureMode
{
    Automatic,
    Copy,
    All
}

public sealed record SelectedTextCaptureCommand(
    SelectedTextCaptureMode Mode = SelectedTextCaptureMode.Automatic,
    PhysicalScreenPoint? PointerPosition = null,
    ExternalTargetToken ExpectedForegroundTarget = default,
    ExternalTargetToken ExpectedFocusedTarget = default);

public interface ISelectedTextUseCases
{
    ValueTask<Result<SelectedText>> CaptureAsync(
        SelectedTextCaptureCommand command,
        CancellationToken cancellationToken = default);
}

public enum SelectionGesture
{
    Drag,
    DoubleClick
}

public sealed record SelectionToolbarOptions(
    bool Translation,
    bool Correction,
    bool Polish,
    bool Summary,
    bool Explanation = true)
{
    public bool HasAnyAction => Translation || Correction || Polish || Summary || Explanation;
}

public sealed record SelectionCapture(
    SelectedText SelectedText,
    SelectionGesture Gesture,
    SelectionToolbarOptions Toolbar);

public readonly record struct SelectionSurfaceState(
    bool IsPointerOverOwnedSurface,
    bool BlocksSelectionCapture);

/// <summary>
/// Presentation-side reactions required by the selection interaction workflow.
/// Implementations marshal to their UI dispatcher and keep all window types private.
/// </summary>
public interface ISelectionInteractionSink
{
    ValueTask<SelectionSurfaceState> InspectSurfaceAsync(
        PhysicalScreenPoint point,
        CancellationToken cancellationToken = default);

    ValueTask OnMonitoringStartedAsync(CancellationToken cancellationToken = default);

    ValueTask OnExternalPointerPressedAsync(
        PhysicalScreenPoint point,
        CancellationToken cancellationToken = default);

    ValueTask OnSelectionCapturedAsync(
        SelectionCapture capture,
        CancellationToken cancellationToken = default);
}

public interface ISelectionInteractionUseCases : IAsyncDisposable
{
    void Start(ISelectionInteractionSink sink);
    void Stop();
}
