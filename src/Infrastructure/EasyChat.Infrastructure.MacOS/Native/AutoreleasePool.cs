namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>
/// Scopes an Objective-C autorelease pool over a block of message sends. A .NET thread carries no
/// pool of its own, so without this every autoreleased temporary would leak for the lifetime of the
/// process.
/// </summary>
internal readonly struct AutoreleasePool : IDisposable
{
    private readonly IntPtr _pool;

    private AutoreleasePool(IntPtr pool) => _pool = pool;

    internal static AutoreleasePool Push() => new(ObjectiveCNative.PushAutoreleasePool());

    public void Dispose() => ObjectiveCNative.PopAutoreleasePool(_pool);
}
