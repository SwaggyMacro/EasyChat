using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Input;
using EasyChat.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Infrastructure.MacOS.Tests.Input;

/// <summary>
/// Every collaborator is a fake, so no test here writes the real pasteboard or injects a keystroke
/// into whatever the developer has in front of them.
/// </summary>
[TestClass]
public sealed class MacSelectedTextCaptureTests
{
    private readonly FakeClipboard _clipboard = new();
    private readonly FakeKeyboardState _keyboard = new();
    private readonly FakeTextDelivery _delivery = new();

    private MacSelectedTextCapture CreateCapture() =>
        new(
            _clipboard,
            _clipboard,
            new FakePointerPosition(),
            _keyboard,
            new FakeTextSelection(),
            _delivery,
            NullLogger<MacSelectedTextCapture>.Instance);

    [TestMethod]
    public async Task ATargetFromAnotherSessionIsRejectedBeforeAnythingIsTouched()
    {
        var captured = await CreateCapture().CaptureAsync(new SelectionCaptureRequest(
            ExpectedForegroundTarget: new ExternalTargetToken("win32:1A2B")));

        Assert.IsTrue(captured.IsFailure);
        Assert.AreEqual("selection.target-invalid", captured.Error.Code);
        Assert.AreEqual(0, _delivery.CommandsSent);
    }

    [TestMethod]
    public async Task HeldShortcutKeysStopTheCaptureBeforeAnyKeystrokeIsInjected()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            Assert.Inconclusive("Selection capture can only be verified on macOS 26 or later.");
            return;
        }

        _keyboard.Pressed = KeyboardKey.LeftMeta;

        var captured = await CreateCapture().CaptureAsync(new SelectionCaptureRequest(
            CopyOnly: true));

        Assert.IsTrue(captured.IsFailure);
        Assert.AreEqual("selection.keyboard-busy", captured.Error.Code);
        Assert.AreEqual(0, _delivery.CommandsSent);
        Assert.IsTrue(_clipboard.Restored, "A captured pasteboard must be put back on every exit.");
    }

    [TestMethod]
    public async Task ADirectOnlyRequestGivesUpRatherThanFallingBackToTheClipboard()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            Assert.Inconclusive("Selection capture can only be verified on macOS 26 or later.");
            return;
        }

        // The test host holds no Accessibility approval, so the direct read finds nothing.
        var captured = await CreateCapture().CaptureAsync(new SelectionCaptureRequest(
            DirectOnly: true));

        Assert.IsTrue(captured.IsFailure);
        Assert.AreEqual("selection.empty", captured.Error.Code);
        Assert.AreEqual(0, _delivery.CommandsSent);
        Assert.IsFalse(_clipboard.Captured, "A direct-only capture must not touch the pasteboard.");
    }

    private sealed class FakeClipboard : IClipboardSnapshots, IClipboardText
    {
        internal bool Captured { get; private set; }

        internal bool Restored { get; private set; }

        public ValueTask<Result<IClipboardChangeToken>> GetChangeTokenAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<IClipboardChangeToken>.Success(new Token()));

        public ValueTask<Result<bool>> IsChangeTokenCurrentAsync(
            IClipboardChangeToken changeToken,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<bool>.Success(true));

        public ValueTask<Result<IClipboardSnapshot>> CaptureAsync(
            CancellationToken cancellationToken = default)
        {
            Captured = true;
            return ValueTask.FromResult(Result<IClipboardSnapshot>.Success(new Snapshot()));
        }

        public ValueTask<Result> RestoreAsync(
            IClipboardSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            Restored = true;
            return ValueTask.FromResult(Result.Success());
        }

        public ValueTask<Result> RestoreIfUnchangedAsync(
            IClipboardSnapshot snapshot,
            IClipboardChangeToken expectedChangeToken,
            CancellationToken cancellationToken = default)
        {
            Restored = true;
            return ValueTask.FromResult(Result.Success());
        }

        public ValueTask<Result<string?>> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<string?>.Success(null));

        public ValueTask<Result> WriteAsync(
            string text,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success());

        private sealed class Token : IClipboardChangeToken;

        private sealed class Snapshot : IClipboardSnapshot
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeKeyboardState : IKeyboardState
    {
        internal KeyboardKey? Pressed { get; set; }

        public bool IsPressed(KeyboardKey key) => Pressed == key;
    }

    private sealed class FakePointerPosition : IPointerPosition
    {
        public PhysicalScreenPoint GetCurrent() => new(10, 20);
    }

    private sealed class FakeTextSelection : ITextSelection
    {
        public ValueTask<Result<TextSelectionRange>> SelectAllAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<TextSelectionRange>.Success(
                new TextSelectionRange(false, 0, 0)));
    }

    private sealed class FakeTextDelivery : ITextDelivery
    {
        internal int CommandsSent { get; private set; }

        public ValueTask<Result> DeliverAsync(
            TextDeliveryRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success());

        public ValueTask<Result> SendCommandAsync(
            StandardTextCommand command,
            CancellationToken cancellationToken = default)
        {
            CommandsSent++;
            return ValueTask.FromResult(Result.Success());
        }

        public ValueTask<Result> SendKeyCombinationAsync(
            string combination,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success());
    }
}
