using EasyChat.Application.Selection;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Selection;
using EasyChat.Shared.Results;

namespace EasyChat.Application.Tests.Selection;

[TestClass]
public sealed class SelectedTextUseCasesTests
{
    [TestMethod]
    public async Task CaptureAsync_AllFallsBackToSelectAllCommandAndPreservesCopyCaptureRequest()
    {
        var sequence = new List<string>();
        var capture = new FakeCapture(sequence);
        var useCases = new SelectedTextUseCases(
            new AvailablePlatformAccess(),
            capture,
            new FakeSelection(sequence, new TextSelectionRange(true, 2, 5)),
            new FakeDelivery(sequence),
            new FakeKeyboardState(),
            new FakeDelay(sequence));

        var result = await useCases.CaptureAsync(new SelectedTextCaptureCommand(
            SelectedTextCaptureMode.All,
            new PhysicalScreenPoint(12, 34),
            new ExternalTargetToken("foreground"),
            new ExternalTargetToken("focused")));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(capture.Request);
        Assert.IsTrue(capture.Request.CopyOnly);
        Assert.IsFalse(capture.Request.CaptureAll);
        Assert.AreEqual(new PhysicalScreenPoint(12, 34), capture.Request.PointerPosition);
        CollectionAssert.AreEqual(
            new[] { "select-all", "command:SelectAll", "delay:50", "capture" },
            sequence);
    }

    [TestMethod]
    public async Task CaptureAsync_WaitsAtMostTwoSecondsForShortcutKeys()
    {
        var sequence = new List<string>();
        var useCases = new SelectedTextUseCases(
            new AvailablePlatformAccess(),
            new FakeCapture(sequence),
            new FakeSelection(sequence, new TextSelectionRange(true, 0, 1)),
            new FakeDelivery(sequence),
            new FakeKeyboardState(alwaysPressed: true),
            new FakeDelay(sequence));

        var result = await useCases.CaptureAsync(new SelectedTextCaptureCommand());

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual("selection.modifier-timeout", result.Error.Code);
        Assert.AreEqual(200, sequence.Count(item => item == "delay:10"));
        Assert.DoesNotContain("capture", sequence);
    }

    private sealed class FakeCapture(List<string> sequence) : ISelectedTextCapture
    {
        public SelectionCaptureRequest? Request { get; private set; }

        public ValueTask<Result<SelectedText>> CaptureAsync(
            SelectionCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            sequence.Add("capture");
            return ValueTask.FromResult(Result<SelectedText>.Success(new SelectedText(
                "selected",
                request.ExpectedForegroundTarget,
                "fake",
                request.PointerPosition)));
        }
    }

    private sealed class FakeSelection(
        List<string> sequence,
        TextSelectionRange range) : ITextSelection
    {
        public ValueTask<Result<TextSelectionRange>> SelectAllAsync(
            CancellationToken cancellationToken = default)
        {
            sequence.Add("select-all");
            return ValueTask.FromResult(Result<TextSelectionRange>.Success(range));
        }
    }

    private sealed class FakeDelivery(List<string> sequence) : ITextDelivery
    {
        public ValueTask<Result> DeliverAsync(
            TextDeliveryRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success());

        public ValueTask<Result> SendKeyCombinationAsync(
            string combination,
            CancellationToken cancellationToken = default)
        {
            sequence.Add($"key:{combination}");
            return ValueTask.FromResult(Result.Success());
        }

        public ValueTask<Result> SendCommandAsync(
            StandardTextCommand command,
            CancellationToken cancellationToken = default)
        {
            sequence.Add($"command:{command}");
            return ValueTask.FromResult(Result.Success());
        }
    }

    private sealed class FakeKeyboardState(bool alwaysPressed = false) : IKeyboardState
    {
        public bool IsPressed(KeyboardKey key) => alwaysPressed;
    }

    private sealed class FakeDelay(List<string> sequence) : ISelectionDelay
    {
        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            sequence.Add($"delay:{delay.TotalMilliseconds:0}");
            return Task.CompletedTask;
        }
    }
}
