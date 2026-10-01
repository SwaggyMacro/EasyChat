using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>
/// Grand Central Dispatch queues, needed because some Apple APIs ask which queue to deliver their
/// callbacks on.
/// </summary>
internal static partial class DispatchNative
{
    private const string LibraryPath = "/usr/lib/libSystem.B.dylib";

    [LibraryImport(LibraryPath, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr dispatch_queue_create(string label, IntPtr attributes);

    [LibraryImport(LibraryPath)]
    internal static partial void dispatch_release(IntPtr queue);

    /// <summary>
    /// Creates a serial queue. Serial matters for a capture callback: audio has to stay in order,
    /// and a concurrent queue would let two buffers be handled at once.
    /// </summary>
    internal static IntPtr CreateSerialQueue(string label) =>
        dispatch_queue_create(label, IntPtr.Zero);
}
