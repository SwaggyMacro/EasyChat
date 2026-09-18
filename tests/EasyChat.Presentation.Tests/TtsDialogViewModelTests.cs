using System.Reactive.Linq;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Speech;
using EasyChat.Presentation.Features.Speech;
using EasyChat.Shared.Results;
using ShadUI;

namespace EasyChat.Presentation.Tests;

[TestClass]
public sealed class TtsDialogViewModelTests
{
    private static readonly TtsLanguageItem English = new(new TtsLanguage(
        "en-US", "English", "United States", "English (United States)",
        string.Empty, "us.png"));

    private static readonly TtsVoice Voice = new(
        "voice-1", "Voice One", "en-US", "Female", [], []);

    [TestMethod]
    public async Task SaveCommand_PublishesSelectedLanguageAndVoiceWithoutCancel()
    {
        var saved = false;
        var cancelled = false;
        var viewModel = CreateEditViewModel(
            (language, voice) =>
            {
                Assert.AreSame(English, language);
                Assert.AreSame(Voice, voice);
                saved = true;
            },
            () => cancelled = true);

        await viewModel.SaveCommand.Execute();

        Assert.IsTrue(saved);
        Assert.IsFalse(cancelled);
    }

    [TestMethod]
    public async Task CancelCommand_InvokesCancelWithoutSaving()
    {
        var saved = false;
        var cancelled = false;
        var viewModel = CreateEditViewModel(
            (_, _) => saved = true,
            () => cancelled = true);

        await viewModel.CancelCommand.Execute();

        Assert.IsFalse(saved);
        Assert.IsTrue(cancelled);
    }

    [TestMethod]
    public async Task PreviewPlayCommand_EnqueuesSelectedVoiceAndRestoresIdleState()
    {
        var tts = new StubTtsUseCases();
        var viewModel = new TtsPreviewInputDialogViewModel(
            new DialogManager(), tts, "provider-1", Voice.Id)
        {
            InputText = "Preview text"
        };

        await viewModel.PlayCommand.Execute();

        Assert.IsNotNull(tts.EnqueuedRequest);
        Assert.AreEqual("Preview text", tts.EnqueuedRequest.Text);
        Assert.AreEqual(Voice.Id, tts.EnqueuedRequest.VoiceId);
        Assert.AreEqual("provider-1", tts.EnqueuedRequest.ProviderId);
        Assert.IsTrue(tts.InterruptCurrent);
        Assert.IsFalse(viewModel.IsPlaying);
    }

    [TestMethod]
    public async Task PreviewCloseCommand_InvokesDismissCallback()
    {
        var dismissed = false;
        var viewModel = new TtsPreviewInputDialogViewModel(
            new DialogManager(), new StubTtsUseCases(), "provider-1", Voice.Id)
        {
            OnDismiss = () => dismissed = true
        };

        await viewModel.CloseCommand.Execute();

        Assert.IsTrue(dismissed);
    }

    private static TtsEditVoiceDialogViewModel CreateEditViewModel(
        Action<TtsLanguageItem, TtsVoice> onSave,
        Action onCancel) => new(
            new DialogManager(),
            new StubTtsUseCases(),
            "provider-1",
            [English],
            [Voice],
            English,
            Voice.Id)
        {
            OnSave = onSave,
            OnCancel = onCancel
        };

    private sealed class StubTtsUseCases : ITtsUseCases
    {
        public TtsSynthesisRequest? EnqueuedRequest { get; private set; }
        public bool InterruptCurrent { get; private set; }

        public IReadOnlyList<TtsProviderDescriptor> GetProviders() =>
            [new("provider-1")];

        public ValueTask<Result<IReadOnlyList<TtsVoice>>> GetVoicesAsync(
            string? providerId = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<IReadOnlyList<TtsVoice>>.Success([Voice]));

        public ValueTask<Result<IReadOnlyList<TtsLanguage>>> GetLanguagesAsync(
            string? providerId = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<IReadOnlyList<TtsLanguage>>.Success([English.Value]));

        public ValueTask<Result<string?>> ResolvePreferredVoiceAsync(
            string languageId,
            string? providerId = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<string?>.Success(Voice.Id));

        public ValueTask<Result<ITtsPlaybackSession>> EnqueueWithSessionAsync(
            TtsSynthesisRequest request,
            bool interruptCurrent = false,
            CancellationToken cancellationToken = default) =>
            RecordSession(request, interruptCurrent);

        public ValueTask<Result<ITtsPlaybackSession>> EnqueueWithSessionAsync(
            TtsSynthesisRequest request,
            bool interruptCurrent,
            AudioPlaybackTarget target,
            CancellationToken cancellationToken = default) =>
            EnqueueWithSessionAsync(request, interruptCurrent, cancellationToken);

        public ValueTask<Result<ITtsPlaybackSession>> EnqueueWithSessionAsync(
            TtsSynthesisRequest request,
            bool interruptCurrent,
            AudioPlaybackTarget target,
            AudioPlaybackContext context,
            CancellationToken cancellationToken = default) =>
            EnqueueWithSessionAsync(request, interruptCurrent, cancellationToken);

        private ValueTask<Result<ITtsPlaybackSession>> RecordSession(
            TtsSynthesisRequest request,
            bool interruptCurrent)
        {
            EnqueuedRequest = request;
            InterruptCurrent = interruptCurrent;
            return ValueTask.FromResult(Result<ITtsPlaybackSession>.Success(
                new StubPlaybackSession(Guid.NewGuid())));
        }

        public ValueTask<Result<AudioTrack>> SynthesizeAsync(
            TtsSynthesisRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result<AudioTrack>.Success(
                new AudioTrack(ReadOnlyMemory<byte>.Empty, "audio/mpeg")));

        public ValueTask<Result> SynthesizeToFileAsync(
            TtsSynthesisRequest request,
            string outputPath,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Result.Success());

        public ValueTask<Result> EnqueueAsync(
            TtsSynthesisRequest request,
            bool interruptCurrent = false,
            CancellationToken cancellationToken = default)
        {
            EnqueuedRequest = request;
            InterruptCurrent = interruptCurrent;
            return ValueTask.FromResult(Result.Success());
        }

        public ValueTask<Result> EnqueueAsync(
            TtsSynthesisRequest request,
            bool interruptCurrent,
            AudioPlaybackTarget target,
            CancellationToken cancellationToken = default)
        {
            EnqueuedRequest = request;
            InterruptCurrent = interruptCurrent;
            return ValueTask.FromResult(Result.Success());
        }

        private sealed class StubPlaybackSession(Guid id) : ITtsPlaybackSession
        {
            public Guid PlaybackId { get; } = id;
            public Task Completion { get; } = Task.CompletedTask;
            public ValueTask StopAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        }
    }
}
