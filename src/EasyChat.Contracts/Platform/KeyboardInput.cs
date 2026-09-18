using EasyChat.Shared.Results;

namespace EasyChat.Contracts.Platform;

/// <summary>
/// Sends physical key transitions through the active platform adapter.
/// </summary>
public interface IKeyboardInput
{
    Result ValidateCombination(string combination);

    ValueTask<Result> PressAsync(
        string combination,
        CancellationToken cancellationToken = default);

    ValueTask<Result> ReleaseAsync(
        string combination,
        CancellationToken cancellationToken = default);

    ValueTask<Result> TapAsync(
        string combination,
        CancellationToken cancellationToken = default);
}
