using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.MacOS.Tests.Platform;

[TestClass]
public sealed class MacPlatformPermissionRequesterTests
{
    [TestMethod]
    public async Task AnApprovalAlreadyInPlaceIsReportedWithoutPrompting()
    {
        var privacy = new FakeMacPrivacyGateway().Returning(MacPrivacyState.Granted);
        var requester = new MacPlatformPermissionRequester(privacy);

        var result = await requester.RequestAsync(PlatformPermission.Accessibility);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PermissionState.Granted, result.Value.State);
        Assert.IsNull(result.Value.Reason);
        Assert.IsEmpty(privacy.Prompted);
    }

    [TestMethod]
    public async Task AnUndecidedApprovalPromptsAndReportsTheStateObservedAfterwards()
    {
        var privacy = new FakeMacPrivacyGateway()
            .Returning(MacPrivacyState.NotDetermined, MacPrivacyState.Granted);
        var requester = new MacPlatformPermissionRequester(privacy);

        var result = await requester.RequestAsync(PlatformPermission.Microphone);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PermissionState.Granted, result.Value.State);
        Assert.Contains(PlatformPermission.Microphone, privacy.Prompted);
    }

    [TestMethod]
    public async Task AnApprovalMacOsHasNotAppliedYetIsDeniedWithTheRestartReason()
    {
        var privacy = new FakeMacPrivacyGateway().Returning(MacPrivacyState.NotDetermined);
        privacy.PromptResult = MacPrivacyState.Granted;
        var requester = new MacPlatformPermissionRequester(privacy);

        var result = await requester.RequestAsync(PlatformPermission.ScreenRecording);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PermissionState.Denied, result.Value.State);
        Assert.IsNotNull(result.Value.Reason);
        StringAssert.Contains(result.Value.Reason, "restart EasyChat");
    }

    [TestMethod]
    public async Task ARefusedApprovalIsDeniedAndPointsAtTheSystemSettingsPane()
    {
        var privacy = new FakeMacPrivacyGateway().Returning(MacPrivacyState.Denied);
        var requester = new MacPlatformPermissionRequester(privacy);

        var result = await requester.RequestAsync(PlatformPermission.InputMonitoring);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PermissionState.Denied, result.Value.State);
        Assert.IsNotNull(result.Value.Reason);
        StringAssert.Contains(result.Value.Reason, "Input Monitoring");
    }

    [TestMethod]
    public Task AnApprovalWithheldBySystemPolicyIsUnsupportedAndIsNeverPrompted() =>
        AssertApprovalIsUnsupported(MacPrivacyState.Restricted);

    [TestMethod]
    public Task APermissionWithoutAPrivacyEntryPointIsUnsupportedAndIsNeverPrompted() =>
        AssertApprovalIsUnsupported(MacPrivacyState.Unavailable);

    private static async Task AssertApprovalIsUnsupported(MacPrivacyState state)
    {
        var privacy = new FakeMacPrivacyGateway().Returning(state);
        var requester = new MacPlatformPermissionRequester(privacy);

        var result = await requester.RequestAsync(PlatformPermission.Microphone);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(PermissionState.Unsupported, result.Value.State);
        Assert.IsEmpty(privacy.Prompted);
    }

    [TestMethod]
    public async Task AFailingNativeRequestBecomesAFailedResultInsteadOfAnException()
    {
        var privacy = new FakeMacPrivacyGateway().Returning(MacPrivacyState.NotDetermined);
        privacy.PromptFault = new InvalidOperationException("AVFoundation is unavailable");
        var requester = new MacPlatformPermissionRequester(privacy);

        var result = await requester.RequestAsync(PlatformPermission.Microphone);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual("platform.permission.microphone.failed", result.Error.Code);
        StringAssert.Contains(result.Error.Message, "AVFoundation is unavailable");
    }

    [TestMethod]
    public async Task ARequestCancelledBeforeItStartsNeverPrompts()
    {
        var privacy = new FakeMacPrivacyGateway().Returning(MacPrivacyState.NotDetermined);
        var requester = new MacPlatformPermissionRequester(privacy);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await requester.RequestAsync(PlatformPermission.Accessibility, cancellation.Token));
        Assert.IsEmpty(privacy.Prompted);
    }

    [TestMethod]
    public async Task ARequestCancelledWhileThePromptIsOpenDoesNotReportAnApproval()
    {
        using var cancellation = new CancellationTokenSource();
        var privacy = new FakeMacPrivacyGateway()
            .Returning(MacPrivacyState.NotDetermined, MacPrivacyState.Granted);
        privacy.CancelDuringPrompt = cancellation;
        var requester = new MacPlatformPermissionRequester(privacy);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await requester.RequestAsync(PlatformPermission.ScreenRecording, cancellation.Token));
    }
}
