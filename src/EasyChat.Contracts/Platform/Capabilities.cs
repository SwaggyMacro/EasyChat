using EasyChat.Shared.Results;

namespace EasyChat.Contracts.Platform;

public enum PlatformCapability
{
    ScreenCapture,
    GlobalHotkeys,
    SelectedTextCapture,
    TextDelivery,
    Clipboard,
    GlobalPointerMonitoring,
    WindowActivation,
    AudioCaptureSources,
    SpeechRecognition,
    AudioPlayback,
    TextServicesFramework
}

public enum CapabilityState
{
    Available,
    PermissionRequired,
    Unsupported
}

public enum PlatformPermission
{
    Accessibility,
    ScreenRecording,
    InputMonitoring,
    Microphone,
    SystemAudioCapture
}

public enum PermissionState
{
    Granted,
    Denied,
    Unsupported
}

public sealed record PermissionStatus(
    PlatformPermission Permission,
    PermissionState State,
    string? Reason = null);

public sealed record CapabilityStatus(
    PlatformCapability Capability,
    CapabilityState State,
    PlatformPermission? RequiredPermission = null,
    string? Reason = null);

public interface IPlatformCapabilities
{
    /// <summary>
    /// Reports current availability without prompting. A permission-gated adapter identifies the
    /// next permission that must be requested.
    /// </summary>
    ValueTask<CapabilityStatus> GetStatusAsync(
        PlatformCapability capability,
        CancellationToken cancellationToken = default);
}

public interface IPlatformPermissionRequester
{
    /// <summary>
    /// Checks or requests a platform permission. A granted result does not imply that the related
    /// capability is effective until <see cref="IPlatformCapabilities"/> is queried again.
    /// </summary>
    ValueTask<Result<PermissionStatus>> RequestAsync(
        PlatformPermission permission,
        CancellationToken cancellationToken = default);
}

public interface IPlatformAccessUseCases
{
    ValueTask<Result<CapabilityStatus>> EnsureAvailableAsync(
        PlatformCapability capability,
        CancellationToken cancellationToken = default);

    ValueTask<Result<PermissionStatus>> EnsurePermissionAsync(
        PlatformPermission permission,
        CancellationToken cancellationToken = default);
}
