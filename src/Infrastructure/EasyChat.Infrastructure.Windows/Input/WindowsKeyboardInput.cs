using System.Runtime.Versioning;
using EasyChat.Contracts.Platform;
using EasyChat.Shared.Results;

namespace EasyChat.Infrastructure.Windows.Input;

[SupportedOSPlatform("windows")]
public sealed class WindowsKeyboardInput : IKeyboardInput
{
    private readonly WindowsNativeInputBackend _native = new();

    public Result ValidateCombination(string combination)
    {
        if (string.IsNullOrWhiteSpace(combination))
        {
            return Result.Failure(new Error(
                "keyboard-input.combination-empty",
                "The automatic key combination is empty."));
        }

        try
        {
            WindowsKeyCombination.Parse(combination);
            return Result.Success();
        }
        catch (Exception exception)
        {
            return Result.Failure(new Error(
                "keyboard-input.combination-invalid",
                exception.Message));
        }
    }

    public ValueTask<Result> PressAsync(
        string combination,
        CancellationToken cancellationToken = default) =>
        SendAsync(combination, WindowsKeyCombination.ParsePress, cancellationToken);

    public ValueTask<Result> ReleaseAsync(
        string combination,
        CancellationToken cancellationToken = default) =>
        SendAsync(combination, WindowsKeyCombination.ParseRelease, cancellationToken);

    public ValueTask<Result> TapAsync(
        string combination,
        CancellationToken cancellationToken = default) =>
        SendAsync(combination, WindowsKeyCombination.Parse, cancellationToken);

    private ValueTask<Result> SendAsync(
        string combination,
        Func<string, IReadOnlyList<WindowsKeyboardInputEvent>> parser,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(combination);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var inputs = parser(combination);
            var sent = _native.SendInputs(inputs);
            return ValueTask.FromResult(sent == inputs.Count
                ? Result.Success()
                : Result.Failure(new Error(
                    "keyboard-input.send-failed",
                    $"Only {sent} of {inputs.Count} key events were sent.")));
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(Result.Failure(new Error(
                "keyboard-input.send-failed",
                exception.Message)));
        }
    }
}
