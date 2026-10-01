using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.MacOS.Tests.Platform;

[TestClass]
public sealed class MacPlatformCapabilitiesTests
{
    [TestMethod]
    public async Task CapabilitiesWithoutAPrivacyGateAreAvailableWithoutConsultingTcc()
    {
        var privacy = new FakeMacPrivacyGateway().Returning(MacPrivacyState.Denied);
        var capabilities = new MacPlatformCapabilities(privacy);

        foreach (var capability in new[]
                 {
                     PlatformCapability.GlobalHotkeys,
                     PlatformCapability.Clipboard,
                     PlatformCapability.SpeechRecognition,
                     PlatformCapability.AudioPlayback
                 })
        {
            var status = await capabilities.GetStatusAsync(capability);
            Assert.AreEqual(CapabilityState.Available, status.State, capability.ToString());
        }

        Assert.IsEmpty(privacy.Checked);
    }

    [TestMethod]
    public async Task EveryCapabilityIsClassifiedByTheMacOsModule()
    {
        var capabilities = new MacPlatformCapabilities(
            new FakeMacPrivacyGateway().Returning(MacPrivacyState.Granted));

        foreach (var capability in Enum.GetValues<PlatformCapability>())
        {
            var status = await capabilities.GetStatusAsync(capability);
            Assert.AreEqual(CapabilityState.Available, status.State, capability.ToString());
        }
    }

    [TestMethod]
    public async Task AGrantedApprovalMakesTheGatedCapabilityAvailable()
    {
        var capabilities = new MacPlatformCapabilities(
            new FakeMacPrivacyGateway().Returning(MacPrivacyState.Granted));

        var status = await capabilities.GetStatusAsync(PlatformCapability.ScreenCapture);

        Assert.AreEqual(CapabilityState.Available, status.State);
    }

    [TestMethod]
    public Task AnUndecidedApprovalNamesThePermissionThatUnlocksTheCapability() =>
        AssertMissingApprovalIsRequestable(MacPrivacyState.NotDetermined);

    [TestMethod]
    public Task ARefusedApprovalStillNamesThePermissionThatUnlocksTheCapability() =>
        AssertMissingApprovalIsRequestable(MacPrivacyState.Denied);

    private static async Task AssertMissingApprovalIsRequestable(MacPrivacyState state)
    {
        var capabilities = new MacPlatformCapabilities(
            new FakeMacPrivacyGateway().Returning(state));

        var status = await capabilities.GetStatusAsync(PlatformCapability.SelectedTextCapture);

        Assert.AreEqual(CapabilityState.PermissionRequired, status.State);
        Assert.AreEqual(PlatformPermission.Accessibility, status.RequiredPermission);
        Assert.IsNotNull(status.Reason);
        StringAssert.Contains(status.Reason, "Accessibility");
    }

    [TestMethod]
    public async Task AnApprovalWithheldBySystemPolicyIsUnsupportedRatherThanRequestable()
    {
        var capabilities = new MacPlatformCapabilities(
            new FakeMacPrivacyGateway().Returning(MacPrivacyState.Restricted));

        var status = await capabilities.GetStatusAsync(PlatformCapability.AudioCaptureSources);

        Assert.AreEqual(CapabilityState.Unsupported, status.State);
        Assert.AreEqual(PlatformPermission.SystemAudioCapture, status.RequiredPermission);
    }

    [TestMethod]
    public async Task ReadingTheCapabilityNeverShowsASystemPrompt()
    {
        var privacy = new FakeMacPrivacyGateway().Returning(MacPrivacyState.NotDetermined);
        var capabilities = new MacPlatformCapabilities(privacy);

        await capabilities.GetStatusAsync(PlatformCapability.ScreenCapture);

        Assert.IsEmpty(privacy.Prompted);
    }

    [TestMethod]
    public async Task ARevokedApprovalIsObservedOnTheNextQuery()
    {
        var capabilities = new MacPlatformCapabilities(new FakeMacPrivacyGateway()
            .Returning(MacPrivacyState.Granted, MacPrivacyState.Denied));

        var granted = await capabilities.GetStatusAsync(PlatformCapability.TextDelivery);
        var revoked = await capabilities.GetStatusAsync(PlatformCapability.TextDelivery);

        Assert.AreEqual(CapabilityState.Available, granted.State);
        Assert.AreEqual(CapabilityState.PermissionRequired, revoked.State);
    }

    [TestMethod]
    public async Task AFailingNativeCheckDegradesToUnsupportedInsteadOfCrashing()
    {
        var capabilities = new MacPlatformCapabilities(new FakeMacPrivacyGateway
        {
            CheckFault = new PlatformNotSupportedException("no privacy database")
        }.Returning(MacPrivacyState.Granted));

        var status = await capabilities.GetStatusAsync(PlatformCapability.GlobalPointerMonitoring);

        Assert.AreEqual(CapabilityState.Unsupported, status.State);
        Assert.IsNotNull(status.Reason);
        StringAssert.Contains(status.Reason, "no privacy database");
    }

    [TestMethod]
    public async Task ACancelledQueryIsCancelledBeforeTouchingTcc()
    {
        var privacy = new FakeMacPrivacyGateway().Returning(MacPrivacyState.Granted);
        var capabilities = new MacPlatformCapabilities(privacy);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await capabilities.GetStatusAsync(PlatformCapability.ScreenCapture, cancellation.Token));
        Assert.IsEmpty(privacy.Checked);
    }
}
