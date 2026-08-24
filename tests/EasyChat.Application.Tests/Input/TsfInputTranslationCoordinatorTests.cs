using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using EasyChat.Application.Input;
using EasyChat.Application.Tests.Settings;
using EasyChat.Contracts.Input;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Translation;
using EasyChat.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyChat.Application.Tests.Input;

[TestClass]
public sealed class TsfInputTranslationCoordinatorTests
{
    [TestMethod]
    public async Task Composition_EmitsPreviewThenCommitsAfterQuietWindow()
    {
        var context = CreateContext(request =>
            Task.FromResult(Result<TranslationResponse>.Success(
                new TranslationResponse($"translated:{request.Text}"))));
        await using var coordinator = context.Coordinator;
        var candidates = new ConcurrentQueue<TsfCandidateChanged>();
        coordinator.CandidateChanged += (_, candidate) => candidates.Enqueue(candidate);
        Assert.IsTrue((await coordinator.StartAsync()).IsSuccess);

        context.Bridge.Emit(new TsfCompositionChanged(
            new TsfSessionToken("session-1"),
            1,
            "hello",
            new PhysicalScreenRegion(20, 30, 2, 20),
            IsSentenceBoundary: false));

        await WaitUntilAsync(() => context.Bridge.Updates.Any(update => !update.IsFinal));
        var committed = await WaitForCandidateAsync(
            candidates,
            candidate => candidate.Status == TsfCandidateStatus.Committed);

        Assert.AreEqual("hello", committed.SourceText);
        Assert.AreEqual("translated:hello", committed.TranslationText);
        Assert.IsTrue(context.Bridge.Updates.Any(update => !update.IsFinal));
        Assert.IsTrue(context.Bridge.Updates.Any(update => update.IsFinal));
    }

    [TestMethod]
    public async Task SentenceBoundary_CommitsWithoutWaitingForQuietWindow()
    {
        var context = CreateContext(request =>
            Task.FromResult(Result<TranslationResponse>.Success(
                new TranslationResponse("final"))));
        await using var coordinator = context.Coordinator;
        var candidates = new ConcurrentQueue<TsfCandidateChanged>();
        coordinator.CandidateChanged += (_, candidate) => candidates.Enqueue(candidate);
        Assert.IsTrue((await coordinator.StartAsync()).IsSuccess);

        context.Bridge.Emit(new TsfCompositionChanged(
            new TsfSessionToken("session-2"),
            1,
            "hello.",
            new PhysicalScreenRegion(20, 30, 2, 20),
            IsSentenceBoundary: true));

        var committed = await WaitForCandidateAsync(
            candidates,
            candidate => candidate.Status == TsfCandidateStatus.Committed);

        Assert.AreEqual("final", committed.TranslationText);
        Assert.IsTrue(context.Bridge.Updates.Any(update => update.IsFinal));
    }

    [TestMethod]
    public async Task NewRevision_InvalidatesLateTranslationResult()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = CreateContext(async (request, cancellationToken) =>
        {
            if (request.Text == "first")
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task;
            }

            return Result<TranslationResponse>.Success(new TranslationResponse($"translated:{request.Text}"));
        });
        await using var coordinator = context.Coordinator;
        Assert.IsTrue((await coordinator.StartAsync()).IsSuccess);

        context.Bridge.Emit(new TsfCompositionChanged(
            new TsfSessionToken("session-3"), 1, "first", null, false));
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        context.Bridge.Emit(new TsfCompositionChanged(
            new TsfSessionToken("session-3"), 2, "second", null, false));
        releaseFirst.TrySetResult();

        await WaitUntilAsync(() => context.Bridge.Updates.Any(update => update.Revision == 2));
        Assert.IsFalse(context.Bridge.Updates.Any(update => update.Revision == 1));
    }

    [TestMethod]
    public async Task TranslationFailure_ReportsFailureAndDoesNotReplaceInput()
    {
        var context = CreateContext(_ =>
            Task.FromResult(Result<TranslationResponse>.Failure(
                new Error("translation.failed", "provider unavailable"))));
        await using var coordinator = context.Coordinator;
        var candidates = new ConcurrentQueue<TsfCandidateChanged>();
        coordinator.CandidateChanged += (_, candidate) => candidates.Enqueue(candidate);
        Assert.IsTrue((await coordinator.StartAsync()).IsSuccess);

        context.Bridge.Emit(new TsfCompositionChanged(
            new TsfSessionToken("session-4"), 1, "keep me", null, true));

        var failed = await WaitForCandidateAsync(
            candidates,
            candidate => candidate.Status == TsfCandidateStatus.Failed);

        Assert.AreEqual("keep me", failed.SourceText);
        Assert.AreEqual("provider unavailable", failed.ErrorMessage);
        Assert.IsEmpty(context.Bridge.Updates);
    }

    [TestMethod]
    public async Task ProtectedInput_IsReportedUnsupportedAndCancelled()
    {
        var context = CreateContext(request =>
            Task.FromResult(Result<TranslationResponse>.Success(
                new TranslationResponse("must not be used"))));
        await using var coordinator = context.Coordinator;
        var candidates = new ConcurrentQueue<TsfCandidateChanged>();
        coordinator.CandidateChanged += (_, candidate) => candidates.Enqueue(candidate);
        Assert.IsTrue((await coordinator.StartAsync()).IsSuccess);

        context.Bridge.Emit(new TsfCompositionChanged(
            new TsfSessionToken("password"), 1, "secret", null, false, IsPasswordField: true));

        var unsupported = await WaitForCandidateAsync(
            candidates,
            candidate => candidate.Status == TsfCandidateStatus.Unsupported);

        Assert.AreEqual("secret", unsupported.SourceText);
        Assert.AreEqual("password", context.Bridge.Cancelled.Single().Session.Value);
        Assert.IsEmpty(context.TranslationRequests);
    }

    private static TestContext CreateContext(
        Func<TranslationRequest, Task<Result<TranslationResponse>>> handler) =>
        CreateContext((request, _) => handler(request));

    private static TestContext CreateContext(
        Func<TranslationRequest, CancellationToken, Task<Result<TranslationResponse>>> handler)
    {
        var settings = new MutableSettingsUseCases(SettingsTestData.CreateBundle() with
        {
            Input = SettingsTestData.CreateBundle().Input with
            {
                TranslationMode = InputTranslationMode.Tsf
            }
        });
        var bridge = new RecordingBridge();
        var translation = new RecordingTranslationUseCases(handler);
        var coordinator = new TsfInputTranslationCoordinator(
            settings,
            new TestLanguageCatalog(),
            translation,
            bridge,
            NullLogger<TsfInputTranslationCoordinator>.Instance);
        return new TestContext(coordinator, bridge, translation.Requests);
    }

    private static async Task<TsfCandidateChanged> WaitForCandidateAsync(
        ConcurrentQueue<TsfCandidateChanged> candidates,
        Func<TsfCandidateChanged, bool> predicate)
    {
        TsfCandidateChanged? match = null;
        await WaitUntilAsync(() =>
        {
            match = candidates.FirstOrDefault(predicate);
            return match is not null;
        });
        return match!;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed record TestContext(
        TsfInputTranslationCoordinator Coordinator,
        RecordingBridge Bridge,
        ConcurrentBag<string> TranslationRequests);

    private sealed class TestLanguageCatalog : ITranslationLanguageCatalog
    {
        public IReadOnlyList<TranslationLanguage> All { get; } =
            [new("auto", "Auto"), new("en", "English"), new("zh-Hans", "Chinese")];

        public TranslationLanguage Get(string id) =>
            All.FirstOrDefault(language => language.Id == id)
            ?? new TranslationLanguage(id, id);
    }

    private sealed class RecordingTranslationUseCases(
        Func<TranslationRequest, CancellationToken, Task<Result<TranslationResponse>>> handler)
        : ITranslationUseCases
    {
        public ConcurrentBag<string> Requests { get; } = [];

        public ITranslationSession Prepare(TranslationProviderSelection? provider = null) =>
            throw new NotSupportedException();

        public Task<Result<TranslationResponse>> TranslateAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request.Text);
            return handler(request, cancellationToken);
        }

        public async IAsyncEnumerable<TranslationEvent> StreamAsync(
            TranslationRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class RecordingBridge : ITextServicesFrameworkBridge
    {
        private readonly object _gate = new();

        public event EventHandler<TsfCompositionChanged>? CompositionChanged;
        public event EventHandler<TsfCompositionEnded>? CompositionEnded;
        public TextServicesFrameworkStatus Status { get; private set; } =
            new(TextServicesFrameworkState.NotActive, "not started");
        public List<TsfTranslationUpdate> Updates { get; } = [];
        public List<(TsfSessionToken Session, long Revision)> Cancelled { get; } = [];

        public ValueTask<Result> StartAsync(CancellationToken cancellationToken = default)
        {
            Status = new(TextServicesFrameworkState.Available, "test");
            return ValueTask.FromResult(Result.Success());
        }

        public ValueTask<Result> SendPreviewAsync(
            TsfTranslationUpdate update,
            CancellationToken cancellationToken = default)
        {
            lock (_gate) Updates.Add(update);
            return ValueTask.FromResult(Result.Success());
        }

        public ValueTask<Result> CommitAsync(
            TsfTranslationUpdate update,
            CancellationToken cancellationToken = default)
        {
            lock (_gate) Updates.Add(update);
            return ValueTask.FromResult(Result.Success());
        }

        public ValueTask<Result> CancelAsync(
            TsfSessionToken session,
            long revision,
            CancellationToken cancellationToken = default)
        {
            lock (_gate) Cancelled.Add((session, revision));
            return ValueTask.FromResult(Result.Success());
        }

        public void Emit(TsfCompositionChanged changed) =>
            CompositionChanged?.Invoke(this, changed);

        public void Emit(TsfCompositionEnded ended) =>
            CompositionEnded?.Invoke(this, ended);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
