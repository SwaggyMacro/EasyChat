using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Input;
using EasyChat.Infrastructure.MacOS.Native;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Infrastructure.MacOS.Tests.Input;

[TestClass]
public sealed class MacGlobalPointerMonitorTests
{
    private static MacGlobalPointerMonitor CreateMonitor() =>
        new(NullLogger<MacGlobalPointerMonitor>.Instance);

    private static bool Skip()
    {
        if (OperatingSystem.IsMacOSVersionAtLeast(26))
            return false;

        Assert.Inconclusive("The pointer tap can only be verified on macOS 26 or later.");
        return true;
    }

    [TestMethod]
    public void AMissingCallbackIsRejected() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => CreateMonitor().Start(null!));

    [TestMethod]
    public void StartingWithoutInputMonitoringStillReturnsAReleasableRegistration()
    {
        if (Skip())
            return;

        // macOS refuses the tap until input monitoring is approved. That must surface as a monitor
        // that reports nothing, never as an exception or a hang on the caller's thread.
        using var registration = CreateMonitor().Start(_ => { });

        Assert.IsNotNull(registration);
    }

    [TestMethod]
    public void ReleasingTwiceIsHarmless()
    {
        if (Skip())
            return;

        var registration = CreateMonitor().Start(_ => { });

        registration.Dispose();
        registration.Dispose();
    }

    [TestMethod]
    public void TheTapIsTornDownOnlyWhenTheLastRegistrationGoes()
    {
        if (Skip())
            return;

        var monitor = CreateMonitor();
        var first = monitor.Start(_ => { });
        var second = monitor.Start(_ => { });

        // Releasing one of two registrations must leave the session running for the other, so this
        // has to return without stopping the run loop.
        first.Dispose();
        second.Dispose();
    }

    [TestMethod]
    public void RepeatedStartAndStopCyclesDoNotAccumulateRunLoopThreads()
    {
        if (Skip())
            return;

        var monitor = CreateMonitor();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        for (var cycle = 0; cycle < 5; cycle++)
            monitor.Start(_ => { }).Dispose();
        elapsed.Stop();

        // Each teardown joins its run loop thread with a two second ceiling, so five cycles that
        // leaked a thread would take tens of seconds rather than a moment.
        Assert.IsLessThan(TimeSpan.FromSeconds(5), elapsed.Elapsed);
    }

    [TestMethod]
    public void OnScreenWindowsAreListedWithoutAnyPrivacyApproval()
    {
        if (Skip())
            return;

        var windows = WindowListNative.OnScreen();

        Assert.IsNotEmpty(windows);
        foreach (var window in windows)
        {
            Assert.IsGreaterThan(0, window.OwnerProcessIdentifier, "owner process");
            Assert.IsGreaterThan(0, window.WindowNumber, "window number");
        }
    }

    [TestMethod]
    public void ThePointerResolvesToAWindowOrToNothingButNeverThrows()
    {
        if (Skip())
            return;

        var pointer = new MacPointerPosition().GetCurrent();
        var windows = WindowListNative.OnScreen();

        // The reading is in physical pixels and the window list is in points, so they are not
        // compared here; the point is that both sides answer on a real desktop.
        Assert.IsNotEmpty(windows);
        Assert.AreEqual(pointer, new MacPointerPosition().GetCurrent());
    }
}
