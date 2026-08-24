using EasyChat.Shared.Results;

namespace EasyChat.Contracts.Platform;

public enum TextServicesFrameworkState
{
    Available = 0,
    RegistrationFailed = 1,
    PipeUnavailable = 2,
    NotActive = 3,
    Unsupported = 4
}

public sealed record TextServicesFrameworkStatus(
    TextServicesFrameworkState State,
    string? Message = null);

public readonly record struct TsfSessionToken(string Value)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);
}

public sealed record TsfCompositionChanged(
    TsfSessionToken Session,
    long Revision,
    string Text,
    PhysicalScreenRegion? CaretRegion,
    bool IsSentenceBoundary,
    bool IsPasswordField = false);

public sealed record TsfCompositionEnded(
    TsfSessionToken Session,
    long Revision,
    bool Accepted);

public sealed record TsfTranslationUpdate(
    TsfSessionToken Session,
    long Revision,
    string Text,
    bool IsFinal);

public interface ITextServicesFrameworkBridge : IAsyncDisposable
{
    event EventHandler<TsfCompositionChanged>? CompositionChanged;
    event EventHandler<TsfCompositionEnded>? CompositionEnded;

    TextServicesFrameworkStatus Status { get; }

    ValueTask<Result> StartAsync(CancellationToken cancellationToken = default);
    ValueTask<Result> SendPreviewAsync(TsfTranslationUpdate update, CancellationToken cancellationToken = default);
    ValueTask<Result> CommitAsync(TsfTranslationUpdate update, CancellationToken cancellationToken = default);
    ValueTask<Result> CancelAsync(TsfSessionToken session, long revision, CancellationToken cancellationToken = default);
}
