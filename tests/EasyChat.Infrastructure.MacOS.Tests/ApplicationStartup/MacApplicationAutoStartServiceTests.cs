using EasyChat.Infrastructure.MacOS.ApplicationStartup;
using EasyChat.Shared.Results;

namespace EasyChat.Infrastructure.MacOS.Tests.ApplicationStartup;

[TestClass]
public sealed class MacApplicationAutoStartServiceTests
{
    [TestMethod]
    public void AnApprovedLoginItemReadsAsEnabled()
    {
        var service = new MacApplicationAutoStartService(
            new FakeMacLoginItemGateway().Reporting(MacLoginItemState.Enabled));

        var result = service.GetEnabled();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Value);
    }

    [TestMethod]
    public void ALoginItemTheUserSwitchedOffInSystemSettingsReadsAsDisabled()
    {
        var service = new MacApplicationAutoStartService(
            new FakeMacLoginItemGateway().Reporting(MacLoginItemState.NotRegistered));

        var result = service.GetEnabled();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.Value);
    }

    [TestMethod]
    public void ARegistrationAwaitingApprovalIsNotReportedAsEnabled()
    {
        var service = new MacApplicationAutoStartService(
            new FakeMacLoginItemGateway().Reporting(MacLoginItemState.RequiresApproval));

        var result = service.GetEnabled();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.Value);
    }

    [TestMethod]
    public void RunningOutsideALaunchableBundleIsAFailureRatherThanAFalseReading()
    {
        var service = new MacApplicationAutoStartService(
            new FakeMacLoginItemGateway().Reporting(MacLoginItemState.NotFound));

        var result = service.GetEnabled();

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual("autostart.bundle-not-found", result.Error.Code);
    }

    [TestMethod]
    public void EnablingRegistersAndConfirmsTheStateWithMacOs()
    {
        // SetEnabled reads the state back once, after registering.
        var loginItem = new FakeMacLoginItemGateway().Reporting(MacLoginItemState.Enabled);
        var service = new MacApplicationAutoStartService(loginItem);

        var result = service.SetEnabled(true);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, loginItem.RegisterCalls);
    }

    [TestMethod]
    public void EnablingReportsTheApprovalMacOsStillNeeds()
    {
        var service = new MacApplicationAutoStartService(
            new FakeMacLoginItemGateway().Reporting(MacLoginItemState.RequiresApproval));

        var result = service.SetEnabled(true);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual("autostart.requires-approval", result.Error.Code);
        StringAssert.Contains(result.Error.Message, "Login Items");
    }

    [TestMethod]
    public void ARegistrationThatDidNotStickIsReportedAsAFailure()
    {
        var service = new MacApplicationAutoStartService(
            new FakeMacLoginItemGateway().Reporting(MacLoginItemState.NotRegistered));

        var result = service.SetEnabled(true);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual("autostart.register-not-effective", result.Error.Code);
    }

    [TestMethod]
    public void ARefusedRegistrationSurfacesTheMacOsErrorAndSkipsVerification()
    {
        var loginItem = new FakeMacLoginItemGateway().Reporting(MacLoginItemState.NotRegistered);
        loginItem.RegisterResult = Result.Failure(new Error(
            "autostart.register-failed",
            "Operation not permitted"));
        var service = new MacApplicationAutoStartService(loginItem);

        var result = service.SetEnabled(true);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual("autostart.register-failed", result.Error.Code);
        StringAssert.Contains(result.Error.Message, "Operation not permitted");
    }

    [TestMethod]
    public void DisablingUnregistersAndConfirmsTheStateWithMacOs()
    {
        var loginItem = new FakeMacLoginItemGateway().Reporting(MacLoginItemState.NotRegistered);
        var service = new MacApplicationAutoStartService(loginItem);

        var result = service.SetEnabled(false);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, loginItem.UnregisterCalls);
    }

    [TestMethod]
    public void ALoginItemThatSurvivesRemovalIsReportedAsAFailure()
    {
        var service = new MacApplicationAutoStartService(
            new FakeMacLoginItemGateway().Reporting(MacLoginItemState.Enabled));

        var result = service.SetEnabled(false);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual("autostart.unregister-not-effective", result.Error.Code);
    }

    [TestMethod]
    public void AFailingNativeReadBecomesAFailedResultInsteadOfAnException()
    {
        var service = new MacApplicationAutoStartService(new FakeMacLoginItemGateway
        {
            ReadFault = new PlatformNotSupportedException("ServiceManagement is unavailable")
        }.Reporting(MacLoginItemState.Enabled));

        var result = service.GetEnabled();

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual("autostart.read-failed", result.Error.Code);
    }
}
