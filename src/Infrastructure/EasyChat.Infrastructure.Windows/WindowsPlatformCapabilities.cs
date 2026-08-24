using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.Windows;

public sealed class WindowsPlatformCapabilities : IPlatformCapabilities
{
    private static readonly IReadOnlySet<PlatformCapability> AvailableCapabilities =
        new HashSet<PlatformCapability>
        {
            PlatformCapability.ScreenCapture,
            PlatformCapability.GlobalHotkeys,
            PlatformCapability.TextDelivery,
            PlatformCapability.Clipboard,
            PlatformCapability.WindowActivation,
            PlatformCapability.GlobalPointerMonitoring,
            PlatformCapability.SelectedTextCapture,
            PlatformCapability.AudioCaptureSources,
            PlatformCapability.SpeechRecognition,
            PlatformCapability.AudioPlayback,
            PlatformCapability.TextServicesFramework
        };

    public ValueTask<CapabilityStatus> GetStatusAsync(
        PlatformCapability capability,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(AvailableCapabilities.Contains(capability)
            ? new CapabilityStatus(capability, CapabilityState.Available)
            : new CapabilityStatus(
                capability,
                CapabilityState.Unsupported,
                Reason: "The capability is not registered in the current Windows module."));
    }
}
