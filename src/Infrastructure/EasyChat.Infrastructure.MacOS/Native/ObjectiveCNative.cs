using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>
/// Minimal Objective-C runtime surface for the Apple capabilities that expose no C entry point.
/// </summary>
/// <remarks>
/// <c>objc_msgSend</c> has no single signature, so it is declared once per shape. The shapes are
/// distinguished by argument count and return type only: on arm64 a pointer and an <c>NSInteger</c>
/// occupy the same register, and <c>nint</c> is an alias of <see cref="IntPtr"/> in C#, so one
/// declaration covers both. <c>BOOL</c> is the exception and keeps its own overload.
/// </remarks>
internal static partial class ObjectiveCNative
{
    private const string LibraryPath = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(LibraryPath, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr GetClass(string name);

    [LibraryImport(LibraryPath, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr GetSelector(string name);

    [LibraryImport(LibraryPath, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendReturningHandle(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibraryPath, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendReturningHandle(
        IntPtr receiver,
        IntPtr selector,
        IntPtr argument);

    [LibraryImport(LibraryPath, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendReturningHandle(
        IntPtr receiver,
        IntPtr selector,
        IntPtr first,
        IntPtr second);

    /// <summary>
    /// For a selector returning <c>int</c> rather than <c>NSInteger</c>, such as
    /// <c>processIdentifier</c>. The upper half of the return register is unspecified for a 32-bit
    /// result, so it must not be read as a pointer-sized value.
    /// </summary>
    [LibraryImport(LibraryPath, EntryPoint = "objc_msgSend")]
    internal static partial int SendReturningInt32(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibraryPath, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendReturningHandle(IntPtr receiver, IntPtr selector, int argument);

    [LibraryImport(LibraryPath, EntryPoint = "objc_msgSend")]
    internal static partial float SendReturningSingle(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibraryPath, EntryPoint = "objc_msgSend")]
    internal static partial nint SendReturningNInt(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibraryPath, EntryPoint = "objc_msgSend")]
    internal static partial nint SendReturningNInt(IntPtr receiver, IntPtr selector, IntPtr argument);

    [LibraryImport(LibraryPath, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SendReturningBool(IntPtr receiver, IntPtr selector, IntPtr argument);

    [LibraryImport(LibraryPath, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SendReturningBool(
        IntPtr receiver,
        IntPtr selector,
        IntPtr first,
        IntPtr second);

    [LibraryImport(LibraryPath, EntryPoint = "objc_msgSend")]
    internal static partial void Send(IntPtr receiver, IntPtr selector);

    [LibraryImport(LibraryPath, EntryPoint = "objc_msgSend")]
    internal static partial void Send(IntPtr receiver, IntPtr selector, IntPtr argument);

    [LibraryImport(LibraryPath, EntryPoint = "objc_msgSend")]
    internal static partial void Send(
        IntPtr receiver,
        IntPtr selector,
        [MarshalAs(UnmanagedType.U1)] bool argument);

    [LibraryImport(LibraryPath, EntryPoint = "objc_msgSend")]
    internal static partial void Send(
        IntPtr receiver,
        IntPtr selector,
        IntPtr first,
        IntPtr second);

    [LibraryImport(LibraryPath, EntryPoint = "objc_msgSend")]
    internal static partial void Send(
        IntPtr receiver,
        IntPtr selector,
        IntPtr first,
        IntPtr second,
        IntPtr third);

    [LibraryImport(LibraryPath, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendReturningHandle(
        IntPtr receiver,
        IntPtr selector,
        IntPtr first,
        IntPtr second,
        IntPtr third);

    [LibraryImport(LibraryPath, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SendReturningBool(
        IntPtr receiver,
        IntPtr selector,
        IntPtr first,
        IntPtr second,
        IntPtr third,
        IntPtr fourth);

    /// <summary>
    /// Opens an autorelease pool. Objective-C APIs answer autoreleased objects and a .NET thread
    /// carries no pool of its own, so a block of message sends that is not bracketed by one leaks
    /// its temporaries for the lifetime of the process.
    /// </summary>
    [LibraryImport(LibraryPath, EntryPoint = "objc_autoreleasePoolPush")]
    internal static partial IntPtr PushAutoreleasePool();

    [LibraryImport(LibraryPath, EntryPoint = "objc_autoreleasePoolPop")]
    internal static partial void PopAutoreleasePool(IntPtr pool);

    /// <summary>Answers whether the receiver implements <paramref name="selector"/>.</summary>
    internal static bool Responds(IntPtr receiver, string selector) =>
        receiver != IntPtr.Zero
        && SendReturningBool(receiver, GetSelector("respondsToSelector:"), GetSelector(selector));

    /// <summary>
    /// Reads an <c>NSString</c> as managed text. The returned buffer belongs to the autorelease
    /// pool, so it is copied immediately.
    /// </summary>
    internal static string? ReadString(IntPtr text)
    {
        if (text == IntPtr.Zero)
            return null;

        // Info-dictionary and attribute reads can answer any object, and sending UTF8String to a
        // non-string would crash rather than return null.
        if (!Responds(text, "UTF8String"))
            return null;

        var utf8 = SendReturningHandle(text, GetSelector("UTF8String"));
        return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
    }
}
