using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS;

/// <summary>
/// Reads and requests macOS privacy state through public APIs only; the TCC database is never read.
/// </summary>
internal sealed class MacSystemPrivacyGateway : IMacPrivacyGateway
{
    public MacPrivacyState Check(PlatformPermission permission)
    {
        EnsureSupportedHost();
        return permission switch
        {
            PlatformPermission.Accessibility => AccessibilityNative.IsProcessTrusted()
                ? MacPrivacyState.Granted
                : MacPrivacyState.NotDetermined,
            PlatformPermission.ScreenRecording or PlatformPermission.SystemAudioCapture =>
                ScreenCaptureAccessNative.HasAccess()
                    ? MacPrivacyState.Granted
                    : MacPrivacyState.NotDetermined,
            PlatformPermission.InputMonitoring => HidAccessNative.CheckListenAccess() switch
            {
                HidAccessType.Granted => MacPrivacyState.Granted,
                HidAccessType.Denied => MacPrivacyState.Denied,
                _ => MacPrivacyState.NotDetermined
            },
            PlatformPermission.Microphone => MicrophoneAccessNative.CheckStatus() switch
            {
                MediaAuthorizationStatus.Authorized => MacPrivacyState.Granted,
                MediaAuthorizationStatus.Denied => MacPrivacyState.Denied,
                MediaAuthorizationStatus.Restricted => MacPrivacyState.Restricted,
                _ => MacPrivacyState.NotDetermined
            },
            _ => MacPrivacyState.Unavailable
        };
    }

    public async ValueTask<MacPrivacyState> PromptAsync(
        PlatformPermission permission,
        CancellationToken cancellationToken)
    {
        EnsureSupportedHost();
        cancellationToken.ThrowIfCancellationRequested();

        switch (permission)
        {
            case PlatformPermission.Accessibility:
                return AccessibilityNative.PromptForTrust()
                    ? MacPrivacyState.Granted
                    : MacPrivacyState.NotDetermined;
            case PlatformPermission.ScreenRecording:
            case PlatformPermission.SystemAudioCapture:
                return ScreenCaptureAccessNative.RequestAccess()
                    ? MacPrivacyState.Granted
                    : MacPrivacyState.NotDetermined;
            case PlatformPermission.InputMonitoring:
                return HidAccessNative.RequestListenAccess()
                    ? MacPrivacyState.Granted
                    : Check(permission);
            case PlatformPermission.Microphone:
                // macOS shows the microphone prompt once; afterwards the recorded answer is returned
                // without any dialog, so asking again would silently repeat the previous decision.
                if (MicrophoneAccessNative.CheckStatus() != MediaAuthorizationStatus.NotDetermined)
                    return Check(permission);
                var granted = await MicrophoneAccessNative.RequestAccessAsync()
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                return granted ? MacPrivacyState.Granted : MacPrivacyState.Denied;
            default:
                return MacPrivacyState.Unavailable;
        }
    }

    private static void EnsureSupportedHost()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
            throw new PlatformNotSupportedException(
                "macOS privacy checks require macOS 26 or later.");
    }
}
