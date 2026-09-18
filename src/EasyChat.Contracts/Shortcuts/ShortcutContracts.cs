using EasyChat.Contracts.Settings;
using EasyChat.Shared.Results;

namespace EasyChat.Contracts.Shortcuts;

public interface IShortcutAction
{
    string ActionType { get; }

    bool PreventConcurrentExecution { get; }

    ValueTask ExecuteAsync(
        ShortcutParameterSettings? parameter,
        CancellationToken cancellationToken = default);
}

public interface IHoldShortcutAction : IShortcutAction
{
    ValueTask ExecutePressedAsync(
        ShortcutParameterSettings? parameter,
        CancellationToken cancellationToken = default);

    ValueTask ExecuteReleasedAsync(
        ShortcutParameterSettings? parameter,
        CancellationToken cancellationToken = default);
}

public interface IAutomaticShortcutAction : IShortcutAction
{
    ValueTask<IReadOnlyList<ShortcutRegistrationIssue>> ConfigureAsync(
        IReadOnlyList<ShortcutEntrySettings> entries,
        CancellationToken cancellationToken = default);

    ValueTask ResetAsync(CancellationToken cancellationToken = default);
}

public sealed record ShortcutRegistrationIssue(
    string ActionType,
    string KeyCombination,
    Error Error);

public sealed record ShortcutRegistrationReport(
    int RequestedCount,
    int RegisteredCount,
    IReadOnlyList<ShortcutRegistrationIssue> Issues);

public interface IShortcutUseCases : IAsyncDisposable
{
    ValueTask<ShortcutRegistrationReport> StartAsync(
        CancellationToken cancellationToken = default);

    ValueTask<ShortcutRegistrationReport> ReloadAsync(
        CancellationToken cancellationToken = default);

    ValueTask<Result> ProbeAsync(
        string keyCombination,
        CancellationToken cancellationToken = default);
}
