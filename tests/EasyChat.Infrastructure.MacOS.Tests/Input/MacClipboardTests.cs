using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Input;
using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS.Tests.Input;

/// <summary>
/// Drives the real general pasteboard, because the whole point of these adapters is the AppKit
/// plumbing and a fake would verify none of it. The developer's own clipboard is captured before the
/// class runs and put back afterwards.
/// </summary>
[TestClass]
public sealed class MacClipboardTests
{
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G'];

    private static MacPasteboardContents? _userClipboard;

    private MacPasteboard _pasteboard = null!;

    [ClassInitialize]
    public static void CaptureUserClipboard(TestContext context)
    {
        if (!Supported)
            return;

        using var pool = AutoreleasePool.Push();
        _userClipboard = new MacPasteboard().Capture();
    }

    [ClassCleanup]
    public static void RestoreUserClipboard()
    {
        if (_userClipboard is null)
            return;

        using var pool = AutoreleasePool.Push();
        new MacPasteboard().Restore(_userClipboard);
        _userClipboard = null;
    }

    [TestInitialize]
    public void CreatePasteboard() => _pasteboard = new MacPasteboard();

    private static bool Supported => OperatingSystem.IsMacOSVersionAtLeast(26);

    private static bool Skip()
    {
        if (Supported)
            return false;

        Assert.Inconclusive("The macOS pasteboard can only be verified on macOS 26 or later.");
        return true;
    }

    [TestMethod]
    public async Task TextWrittenToThePasteboardIsReadBackUnchanged()
    {
        if (Skip())
            return;

        var clipboard = new MacClipboardText(_pasteboard);
        var written = await clipboard.WriteAsync("EasyChat 剪贴板 round-trip");

        Assert.IsTrue(written.IsSuccess, written.Error.Message);
        var read = await clipboard.ReadAsync();
        Assert.IsTrue(read.IsSuccess, read.Error.Message);
        Assert.AreEqual("EasyChat 剪贴板 round-trip", read.Value);
    }

    [TestMethod]
    public async Task AChangeTokenStopsBeingCurrentAfterAnyWrite()
    {
        if (Skip())
            return;

        var snapshots = new MacClipboardSnapshots(_pasteboard);
        var text = new MacClipboardText(_pasteboard);
        await text.WriteAsync("before");

        var token = await snapshots.GetChangeTokenAsync();
        Assert.IsTrue(token.IsSuccess, token.Error.Message);
        var current = await snapshots.IsChangeTokenCurrentAsync(token.Value);
        Assert.IsTrue(current.IsSuccess);
        Assert.IsTrue(current.Value, "The token should still be current before any write.");

        await text.WriteAsync("after");
        var stale = await snapshots.IsChangeTokenCurrentAsync(token.Value);
        Assert.IsTrue(stale.IsSuccess);
        Assert.IsFalse(stale.Value, "The token should be stale once the pasteboard changed.");
    }

    [TestMethod]
    public async Task ASnapshotPutsTheOriginalContentBack()
    {
        if (Skip())
            return;

        var snapshots = new MacClipboardSnapshots(_pasteboard);
        var text = new MacClipboardText(_pasteboard);
        await text.WriteAsync("the user's own text");

        var snapshot = await snapshots.CaptureAsync();
        Assert.IsTrue(snapshot.IsSuccess, snapshot.Error.Message);
        await text.WriteAsync("EasyChat's temporary text");

        var restored = await snapshots.RestoreAsync(snapshot.Value);
        Assert.IsTrue(restored.IsSuccess, restored.Error.Message);
        var read = await text.ReadAsync();
        Assert.AreEqual("the user's own text", read.Value);
    }

    [TestMethod]
    public async Task AConditionalRestorePutsTheOriginalBackWhenNobodyElseWrote()
    {
        if (Skip())
            return;

        var snapshots = new MacClipboardSnapshots(_pasteboard);
        var text = new MacClipboardText(_pasteboard);
        await text.WriteAsync("the user's own text");

        var snapshot = await snapshots.CaptureAsync();
        await text.WriteAsync("EasyChat's temporary text");
        var token = await snapshots.GetChangeTokenAsync();

        var restored = await snapshots.RestoreIfUnchangedAsync(snapshot.Value, token.Value);

        Assert.IsTrue(restored.IsSuccess, restored.Error.Message);
        Assert.AreEqual("the user's own text", (await text.ReadAsync()).Value);
    }

    [TestMethod]
    public async Task AConditionalRestoreLeavesContentTheUserCopiedInTheMeantimeAlone()
    {
        if (Skip())
            return;

        var snapshots = new MacClipboardSnapshots(_pasteboard);
        var text = new MacClipboardText(_pasteboard);
        await text.WriteAsync("the user's own text");

        var snapshot = await snapshots.CaptureAsync();
        await text.WriteAsync("EasyChat's temporary text");
        var token = await snapshots.GetChangeTokenAsync();

        // The user copies something new while EasyChat is still working.
        await text.WriteAsync("what the user just copied");

        var restored = await snapshots.RestoreIfUnchangedAsync(snapshot.Value, token.Value);

        Assert.IsTrue(restored.IsSuccess, restored.Error.Message);
        Assert.AreEqual("what the user just copied", (await text.ReadAsync()).Value);
    }

    [TestMethod]
    public async Task AnImageFrameReachesThePasteboardAsPng()
    {
        if (Skip())
            return;

        var written = await new MacClipboardImage(_pasteboard).WriteAsync(CreateFrame());

        Assert.IsTrue(written.IsSuccess, written.Error.Message);

        using var pool = AutoreleasePool.Push();
        var items = PasteboardNative.ReadItems(out _);
        Assert.HasCount(1, items);
        Assert.IsTrue(
            items[0].Representations.TryGetValue(PasteboardType.Png, out var png),
            "The pasteboard should carry a public.png representation.");
        Assert.IsTrue(
            png!.Take(PngSignature.Length).SequenceEqual(PngSignature),
            "The pasteboard data should be a real PNG stream.");
    }

    [TestMethod]
    public async Task AFrameThatIsNotBgraIsRejectedRatherThanEncodedWrongly()
    {
        var frame = new ImageFrame(2, 2, 8, 96, 96, new byte[16], (ImagePixelFormat)99);

        var written = await new MacClipboardImage(new MacPasteboard()).WriteAsync(frame);

        Assert.IsTrue(written.IsFailure);
        Assert.AreEqual("clipboard.image-format-unsupported", written.Error.Code);
    }

    [TestMethod]
    public async Task ASnapshotFromAnotherServiceIsRejected()
    {
        var snapshots = new MacClipboardSnapshots(new MacPasteboard());

        var restored = await snapshots.RestoreAsync(new ForeignSnapshot());

        Assert.IsTrue(restored.IsFailure);
        Assert.AreEqual("clipboard.snapshot-invalid", restored.Error.Code);
    }

    [TestMethod]
    public async Task AChangeTokenFromAnotherServiceIsRejected()
    {
        var snapshots = new MacClipboardSnapshots(new MacPasteboard());

        var current = await snapshots.IsChangeTokenCurrentAsync(new ForeignChangeToken());

        Assert.IsTrue(current.IsFailure);
        Assert.AreEqual("clipboard.change-token-invalid", current.Error.Code);
    }

    private static ImageFrame CreateFrame()
    {
        const int width = 4;
        const int height = 3;
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 0x20;
            pixels[index + 1] = 0x40;
            pixels[index + 2] = 0x80;
            pixels[index + 3] = 0xFF;
        }

        return new ImageFrame(width, height, width * 4, 96, 96, pixels);
    }

    private sealed class ForeignSnapshot : IClipboardSnapshot
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ForeignChangeToken : IClipboardChangeToken;
}
