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

/// <summary>
/// A key transition observed while a shortcut is being recorded. Observers must not consume the
/// native event; the platform continues dispatching it to the owning application.
/// </summary>
public sealed record GlobalKeyboardEvent(
    string Key,
    ShortcutModifiers Modifiers,
    bool IsPressed);

public interface IKeyboardMonitorRegistration : IDisposable;

public interface IGlobalKeyboardMonitor
{
    IKeyboardMonitorRegistration Start(Action<GlobalKeyboardEvent> callback);
}
