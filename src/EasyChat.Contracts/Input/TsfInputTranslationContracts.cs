using EasyChat.Contracts.Platform;
using EasyChat.Shared.Results;

namespace EasyChat.Contracts.Input;

public enum TsfCandidateStatus
{
    Hidden = 0,
    Translating = 1,
    Preview = 2,
    Committed = 3,
    Failed = 4,
    Unsupported = 5
}

public sealed record TsfCandidateChanged(
    TsfSessionToken Session,
    long Revision,
    string SourceText,
    string TranslationText,
    TsfCandidateStatus Status,
    PhysicalScreenRegion? CaretRegion,
    string? ErrorMessage = null);

public interface ITsfInputTranslationUseCases : IAsyncDisposable
{
    event EventHandler<TsfCandidateChanged>? CandidateChanged;

    TextServicesFrameworkStatus Status { get; }

    ValueTask<Result> StartAsync(CancellationToken cancellationToken = default);
}
