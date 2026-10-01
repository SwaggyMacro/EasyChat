using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.MacOS;

/// <summary>
/// Reports macOS capability availability from the live privacy state, without ever prompting. A
/// capability macOS gates behind an approval is never reported as available before that approval is
/// in place.
/// </summary>
public sealed class MacPlatformCapabilities : IPlatformCapabilities
{
    private static readonly IReadOnlyDictionary<PlatformCapability, PlatformPermission> GatedCapabilities =
        new Dictionary<PlatformCapability, PlatformPermission>
        {
            [PlatformCapability.ScreenCapture] = PlatformPermission.ScreenRecording,
            [PlatformCapability.SelectedTextCapture] = PlatformPermission.Accessibility,
            [PlatformCapability.TextDelivery] = PlatformPermission.Accessibility,
            [PlatformCapability.WindowActivation] = PlatformPermission.Accessibility,
            [PlatformCapability.GlobalPointerMonitoring] = PlatformPermission.InputMonitoring,
            [PlatformCapability.AudioCaptureSources] = PlatformPermission.SystemAudioCapture
        };

    /// <summary>
    /// Capabilities macOS grants to every application: Carbon hot keys, the pasteboard, the bundled
    /// speech recognition engine and audio output all run without a privacy approval.
    /// </summary>
    private static readonly IReadOnlySet<PlatformCapability> UngatedCapabilities =
        new HashSet<PlatformCapability>
        {
            PlatformCapability.GlobalHotkeys,
            PlatformCapability.Clipboard,
            PlatformCapability.SpeechRecognition,
            PlatformCapability.AudioPlayback
        };

    private readonly IMacPrivacyGateway _privacy;

    public MacPlatformCapabilities()
        : this(new MacSystemPrivacyGateway())
    {
    }

    internal MacPlatformCapabilities(IMacPrivacyGateway privacy)
    {
        ArgumentNullException.ThrowIfNull(privacy);
        _privacy = privacy;
    }

    public ValueTask<CapabilityStatus> GetStatusAsync(
        PlatformCapability capability,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (UngatedCapabilities.Contains(capability))
            return ValueTask.FromResult(new CapabilityStatus(capability, CapabilityState.Available));

        if (!GatedCapabilities.TryGetValue(capability, out var permission))
        {
            return ValueTask.FromResult(new CapabilityStatus(
                capability,
                CapabilityState.Unsupported,
                Reason: "The capability is not registered in the current macOS module."));
        }

        MacPrivacyState state;
        try
        {
            state = _privacy.Check(permission);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ValueTask.FromResult(new CapabilityStatus(
                capability,
                CapabilityState.Unsupported,
                permission,
                $"The macOS privacy state for {permission} could not be read: {exception.Message}"));
        }

        var reason = MacPrivacyReasons.Describe(permission, state);
        return ValueTask.FromResult(state switch
        {
            MacPrivacyState.Granted => new CapabilityStatus(capability, CapabilityState.Available),
            MacPrivacyState.Restricted or MacPrivacyState.Unavailable => new CapabilityStatus(
                capability,
                CapabilityState.Unsupported,
                permission,
                reason),
            _ => new CapabilityStatus(
                capability,
                CapabilityState.PermissionRequired,
                permission,
                reason)
        });
    }
}
