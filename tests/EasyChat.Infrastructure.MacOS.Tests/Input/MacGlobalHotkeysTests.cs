using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Input;
using EasyChat.Infrastructure.MacOS.Native;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Infrastructure.MacOS.Tests.Input;

[TestClass]
public sealed class MacGlobalHotkeysTests
{
    /// <summary>
    /// Every modifier at once on a function key: registered only for the length of a test, and
    /// chosen so it cannot collide with anything the developer has bound.
    /// </summary>
    private static readonly ShortcutGesture Unlikely = new(
        "F12",
        ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift
        | ShortcutModifiers.Meta);

    private static MacGlobalHotkeys CreateHotkeys() =>
        new(NullLogger<MacGlobalHotkeys>.Instance);

    private static bool Skip()
    {
        if (OperatingSystem.IsMacOSVersionAtLeast(26))
            return false;

        Assert.Inconclusive("Carbon hot keys can only be verified on macOS 26 or later.");
        return true;
    }

    [TestMethod]
    public async Task AGestureMacOsHasNoKeyForIsRejected()
    {
        var registered = await CreateHotkeys().RegisterAsync(
            new ShortcutGesture("NotAKey", ShortcutModifiers.Meta),
            _ => ValueTask.CompletedTask);

        Assert.IsTrue(registered.IsFailure);
        Assert.AreEqual("hotkey.gesture-unsupported", registered.Error.Code);
    }

    [TestMethod]
    public async Task AShortcutRegistersAndReleasesCleanly()
    {
        if (Skip())
            return;

        var hotkeys = CreateHotkeys();
        var registered = await hotkeys.RegisterAsync(Unlikely, _ => ValueTask.CompletedTask);

        Assert.IsTrue(registered.IsSuccess, registered.Error.Message);
        registered.Value.Dispose();

        // Registering again only succeeds if the first registration really was released.
        var again = await hotkeys.RegisterAsync(Unlikely, _ => ValueTask.CompletedTask);
        Assert.IsTrue(again.IsSuccess, again.Error.Message);
        again.Value.Dispose();
    }

    [TestMethod]
    public async Task ReleasingTwiceIsHarmless()
    {
        if (Skip())
            return;

        var registered = await CreateHotkeys().RegisterAsync(Unlikely, _ => ValueTask.CompletedTask);
        Assert.IsTrue(registered.IsSuccess, registered.Error.Message);

        registered.Value.Dispose();
        registered.Value.Dispose();
    }

    [TestMethod]
    public async Task AShortcutAlreadyTakenIsReportedAsAConflict()
    {
        if (Skip())
            return;

        var hotkeys = CreateHotkeys();
        var first = await hotkeys.RegisterAsync(Unlikely, _ => ValueTask.CompletedTask);
        Assert.IsTrue(first.IsSuccess, first.Error.Message);

        try
        {
            var second = await hotkeys.RegisterAsync(Unlikely, _ => ValueTask.CompletedTask);
            Assert.IsTrue(second.IsFailure, "The same shortcut must not register twice.");
            Assert.AreEqual("hotkey.conflict", second.Error.Code);
        }
        finally
        {
            first.Value.Dispose();
        }
    }

    [TestMethod]
    public async Task ProbingLeavesTheShortcutFreeForTheRealRegistration()
    {
        if (Skip())
            return;

        var hotkeys = CreateHotkeys();
        var probed = await hotkeys.ProbeAsync(Unlikely);

        Assert.IsTrue(probed.IsSuccess, probed.Error.Message);
        var registered = await hotkeys.RegisterAsync(Unlikely, _ => ValueTask.CompletedTask);
        Assert.IsTrue(registered.IsSuccess, "A probe must not keep the shortcut.");
        registered.Value.Dispose();
    }

    [TestMethod]
    public async Task SeveralShortcutsCoexist()
    {
        if (Skip())
            return;

        var hotkeys = CreateHotkeys();
        var all = ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Shift
                  | ShortcutModifiers.Meta;
        var registrations = new List<IHotkeyRegistration>();
        try
        {
            foreach (var key in new[] { "F9", "F10", "F11" })
            {
                var registered = await hotkeys.RegisterAsync(
                    new ShortcutGesture(key, all),
                    _ => ValueTask.CompletedTask);
                Assert.IsTrue(registered.IsSuccess, $"{key}: {registered.Error.Message}");
                registrations.Add(registered.Value);
            }
        }
        finally
        {
            foreach (var registration in registrations)
                registration.Dispose();
        }
    }

    [TestMethod]
    public async Task AHoldShortcutRegistersBothEdges()
    {
        if (Skip())
            return;

        var registered = await CreateHotkeys().RegisterHoldAsync(
            Unlikely,
            _ => ValueTask.CompletedTask,
            _ => ValueTask.CompletedTask);

        Assert.IsTrue(registered.IsSuccess, registered.Error.Message);
        registered.Value.Dispose();
    }

    [TestMethod]
    public void AGestureResolvesToAPhysicalKeyPositionAndCarbonModifiers()
    {
        Assert.IsTrue(MacKeyCombination.TryResolve(
            new ShortcutGesture("A", ShortcutModifiers.Meta | ShortcutModifiers.Shift),
            out var keystroke));

        Assert.AreEqual(0, keystroke.VirtualKey, "kVK_ANSI_A");
        Assert.AreEqual(EventModifiers.Command | EventModifiers.Shift, keystroke.Modifiers);
        Assert.AreEqual(
            CarbonModifiers.Command | CarbonModifiers.Shift,
            MacKeyCombination.ToCarbon(keystroke.Modifiers));
    }

    [TestMethod]
    public void EveryModifierHasACarbonEquivalent() =>
        Assert.AreEqual(
            CarbonModifiers.Control | CarbonModifiers.Option | CarbonModifiers.Shift
            | CarbonModifiers.Command,
            MacKeyCombination.ToCarbon(
                EventModifiers.Control | EventModifiers.Option | EventModifiers.Shift
                | EventModifiers.Command));
}
