using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.MacOS.Tests.Platform;

/// <summary>
/// Replays a scripted sequence of TCC answers so the adapters can be exercised against every
/// outcome, including an approval that only takes effect on the next check.
/// </summary>
internal sealed class FakeMacPrivacyGateway : IMacPrivacyGateway
{
    private readonly Queue<MacPrivacyState> _checks = new();

    internal MacPrivacyState PromptResult { get; set; } = MacPrivacyState.NotDetermined;

    internal Exception? CheckFault { get; set; }

    internal Exception? PromptFault { get; set; }

    internal List<PlatformPermission> Checked { get; } = [];

    internal List<PlatformPermission> Prompted { get; } = [];

    internal CancellationTokenSource? CancelDuringPrompt { get; set; }

    internal FakeMacPrivacyGateway Returning(params MacPrivacyState[] states)
    {
        foreach (var state in states)
            _checks.Enqueue(state);
        return this;
    }

    public MacPrivacyState Check(PlatformPermission permission)
    {
        Checked.Add(permission);
        if (CheckFault is { } fault)
            throw fault;

        return _checks.Count > 1 ? _checks.Dequeue() : _checks.Peek();
    }

    public ValueTask<MacPrivacyState> PromptAsync(
        PlatformPermission permission,
        CancellationToken cancellationToken)
    {
        Prompted.Add(permission);
        cancellationToken.ThrowIfCancellationRequested();
        if (PromptFault is { } fault)
            throw fault;

        CancelDuringPrompt?.Cancel();
        return ValueTask.FromResult(PromptResult);
    }
}
