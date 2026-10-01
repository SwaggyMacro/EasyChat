using EasyChat.Shared.Results;

namespace EasyChat.Infrastructure.MacOS.Tests.ApplicationStartup;

/// <summary>
/// Replays login item states so the adapter can be exercised without registering a real login item
/// on the developer's machine.
/// </summary>
internal sealed class FakeMacLoginItemGateway : IMacLoginItemGateway
{
    private readonly Queue<MacLoginItemState> _states = new();

    internal Result RegisterResult { get; set; } = Result.Success();

    internal Result UnregisterResult { get; set; } = Result.Success();

    internal Exception? ReadFault { get; set; }

    internal int RegisterCalls { get; private set; }

    internal int UnregisterCalls { get; private set; }

    internal FakeMacLoginItemGateway Reporting(params MacLoginItemState[] states)
    {
        foreach (var state in states)
            _states.Enqueue(state);
        return this;
    }

    public MacLoginItemState Read()
    {
        if (ReadFault is { } fault)
            throw fault;

        return _states.Count > 1 ? _states.Dequeue() : _states.Peek();
    }

    public Result Register()
    {
        RegisterCalls++;
        return RegisterResult;
    }

    public Result Unregister()
    {
        UnregisterCalls++;
        return UnregisterResult;
    }
}
