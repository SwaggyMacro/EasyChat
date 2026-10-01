using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.MacOS;

/// <summary>
/// User-facing explanation for a macOS privacy state, including the System Settings pane that owns
/// the approval and the relaunch macOS needs before some approvals take effect.
/// </summary>
internal static class MacPrivacyReasons
{
    internal static string? Describe(PlatformPermission permission, MacPrivacyState state)
    {
        var label = Label(permission);
        return state switch
        {
            MacPrivacyState.Granted => null,
            MacPrivacyState.NotDetermined =>
                $"EasyChat does not have {label} access yet. Grant it in {Pane(permission)}; " +
                "if it was just granted, restart EasyChat so macOS applies the change.",
            MacPrivacyState.Denied =>
                $"{label} access was denied to EasyChat. Re-enable it in {Pane(permission)}, " +
                "then restart EasyChat.",
            MacPrivacyState.Restricted =>
                $"{label} access is withheld by system policy and cannot be granted by the user.",
            _ => $"macOS exposes no privacy entry point for {label} access."
        };
    }

    private static string Label(PlatformPermission permission) => permission switch
    {
        PlatformPermission.Accessibility => "Accessibility",
        PlatformPermission.ScreenRecording => "Screen Recording",
        PlatformPermission.SystemAudioCapture => "System Audio Recording",
        PlatformPermission.InputMonitoring => "Input Monitoring",
        PlatformPermission.Microphone => "Microphone",
        _ => permission.ToString()
    };

    private static string Pane(PlatformPermission permission) => permission switch
    {
        PlatformPermission.Accessibility =>
            "System Settings > Privacy & Security > Accessibility",
        PlatformPermission.ScreenRecording or PlatformPermission.SystemAudioCapture =>
            "System Settings > Privacy & Security > Screen & System Audio Recording",
        PlatformPermission.InputMonitoring =>
            "System Settings > Privacy & Security > Input Monitoring",
        PlatformPermission.Microphone =>
            "System Settings > Privacy & Security > Microphone",
        _ => "System Settings > Privacy & Security"
    };
}
