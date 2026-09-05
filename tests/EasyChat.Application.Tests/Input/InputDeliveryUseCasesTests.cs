using EasyChat.Application.Input;
using EasyChat.Contracts.Input;
using EasyChat.Contracts.Platform;
using EasyChat.Shared.Results;

namespace EasyChat.Application.Tests.Input;

[TestClass]
public sealed class InputDeliveryUseCasesTests
{
    [TestMethod]
    public async Task DeliverAsync_PreservesReplaceDeliveryAndAfterKeyOrder()
    {
        var sequence = new List<string>();
        var delivery = new FakeTextDelivery(sequence);
        var useCases = new InputDeliveryUseCases(
            new AvailablePlatformAccess(),
            new FakeWindowFocus(sequence),
            new FakeTextSelection(sequence, new TextSelectionRange(true, 2, 7)),
            delivery,
            new FakeDelay(sequence));
        var target = new ExternalTargetToken("target");

        var result = await useCases.DeliverAsync(new InputDeliveryRequest(
            "translated",
            target,
            TextDeliveryMode.Paste,
            TimeSpan.FromMilliseconds(12),
            ReplaceCurrentInput: true,
            BeforeKey: "ignored",
            AfterKey: "Enter"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            new TextDeliveryRequest(
                "translated",
                target,
                TextDeliveryMode.Paste,
                TimeSpan.FromMilliseconds(12)),
            delivery.Request);
        CollectionAssert.AreEqual(
            new[]
            {
                "focus", "delay:100", "select-all", "command:SelectAll",
                "delay:50", "command:Delete", "delay:50", "deliver:Paste", "key:Enter"
            },
            sequence);
    }

    [TestMethod]
    public async Task DeliverAsync_FocusFailureStopsBeforeInputMutation()
    {
        var sequence = new List<string>();
        var useCases = new InputDeliveryUseCases(
            new AvailablePlatformAccess(),
            new FakeWindowFocus(sequence, Result.Failure(
                new Error("window.focus-failed", "not focused"))),
            new FakeTextSelection(sequence, new TextSelectionRange(true, 0, 1)),
            new FakeTextDelivery(sequence),
            new FakeDelay(sequence));

        var result = await useCases.DeliverAsync(new InputDeliveryRequest(
            "text",
            new ExternalTargetToken("target"),
            TextDeliveryMode.Message,
            TimeSpan.Zero));

        Assert.AreEqual("window.focus-failed", result.Error.Code);
        CollectionAssert.AreEqual(new[] { "focus" }, sequence);
    }

    private sealed class FakeWindowFocus(
        List<string> sequence,
        Result? focusResult = null) : IWindowFocus
    {
        public ValueTask<Result<ExternalTargetToken>> GetForegroundTargetAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<ExternalTargetToken>.Success(ExternalTargetToken.None));

        public ValueTask<Result<ExternalTargetToken>> GetFocusedTargetAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<ExternalTargetToken>.Success(ExternalTargetToken.None));

        public ValueTask<Result> EnsureFocusedAsync(
            ExternalTargetToken target,
            CancellationToken cancellationToken = default)
        {
            sequence.Add("focus");
            return ValueTask.FromResult(focusResult ?? Result.Success());
        }

        public ValueTask<Result> ConfigureNoActivateAsync(
            ExternalTargetToken target,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success());
    }

    private sealed class FakeTextSelection(
        List<string> sequence,
        TextSelectionRange selection) : ITextSelection
    {
        public ValueTask<Result<TextSelectionRange>> SelectAllAsync(
            CancellationToken cancellationToken = default)
        {
            sequence.Add("select-all");
            return ValueTask.FromResult(Result<TextSelectionRange>.Success(selection));
        }
    }

    private sealed class FakeTextDelivery(List<string> sequence) : ITextDelivery
    {
        public TextDeliveryRequest? Request { get; private set; }

        public ValueTask<Result> DeliverAsync(
            TextDeliveryRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            sequence.Add($"deliver:{request.Mode}");
            return ValueTask.FromResult(Result.Success());
        }

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

    private sealed class FakeDelay(List<string> sequence) : IInputDeliveryDelay
    {
        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
        {
            sequence.Add($"delay:{milliseconds}");
            return Task.CompletedTask;
        }
    }
}
