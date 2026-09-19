using System.Runtime.Versioning;
using EasyChat.Infrastructure.Windows.Audio;
using EasyChat.Infrastructure.Windows.Speech;

namespace EasyChat.Infrastructure.Windows.Tests.Speech;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsAudioCableRoutingTests
{
    [TestMethod]
    public void CableEndpointNamesAreMatchedCaseInsensitively()
    {
        Assert.IsTrue(WindowsAudioCaptureSourceCatalog.IsVirtualCableOutputName(
            "CABLE Output (VB-Audio Virtual Cable)"));
        Assert.IsTrue(WindowsAudioPlaybackDeviceCatalog.IsVirtualCableName(
            "VB-Audio Cable Input"));
        Assert.IsTrue(WindowsAudioPlaybackDeviceCatalog.IsVirtualCableName(
            "CABLE In 16 Ch (VB-Audio Virtual Cable)"));
        Assert.IsFalse(WindowsAudioPlaybackDeviceCatalog.IsVirtualCableName("Speakers"));
    }

    [TestMethod]
    public void CaptureDeviceTokenRoundTripsOpaqueDeviceId()
    {
        var token = WindowsAudioCaptureSourceCatalog.FromCaptureDeviceId(
            "{0.0.1.00000000}.{device-id}");

        Assert.IsTrue(WindowsAudioCaptureSourceCatalog.TryGetCaptureDeviceId(
            token,
            out var deviceId));
        Assert.AreEqual("{0.0.1.00000000}.{device-id}", deviceId);
    }

    [TestMethod]
    public void PlaybackDeviceTokenRetainsNameForEnumerationFallback()
    {
        var token = WindowsAudioPlaybackDeviceCatalog.FromDeviceId("42", "Speakers");

        Assert.AreEqual("windows:playback:42", token.Value);
        Assert.AreEqual("Speakers", token.DeviceName);
    }
}
