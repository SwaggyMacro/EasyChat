using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>
/// Defines an Objective-C class at runtime.
/// </summary>
/// <remarks>
/// Some Apple APIs deliver their results to a delegate object conforming to a protocol, with no
/// block-based alternative — ScreenCaptureKit's audio output is one. Receiving those callbacks means
/// there has to be a real Objective-C class with real methods, which is what this builds: the
/// method implementations are ordinary static managed functions exported as function pointers.
///
/// A class registered this way lives for the process, so each one is built once and reused.
/// </remarks>
internal static partial class ObjectiveCClassBuilder
{
    private const string LibraryPath = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(LibraryPath, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_allocateClassPair(IntPtr superclass, string name, nint extraBytes);

    [LibraryImport(LibraryPath)]
    private static partial void objc_registerClassPair(IntPtr type);

    [LibraryImport(LibraryPath, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool class_addMethod(IntPtr type, IntPtr selector, IntPtr implementation, string types);

    [LibraryImport(LibraryPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool class_addProtocol(IntPtr type, IntPtr protocol);

    [LibraryImport(LibraryPath, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getProtocol(string name);

    [LibraryImport(LibraryPath)]
    private static partial IntPtr class_createInstance(IntPtr type, nint extraBytes);

    /// <summary>One method to graft onto a runtime class.</summary>
    /// <param name="Selector">The Objective-C selector, including its colons.</param>
    /// <param name="Implementation">A function pointer whose first two arguments are self and _cmd.</param>
    /// <param name="TypeEncoding">
    /// The Objective-C type encoding of the method, for example <c>v@:@@q</c> for a method returning
    /// void and taking an object, an object and a long. Getting this wrong corrupts the call frame,
    /// so each use states what the encoding means.
    /// </param>
    internal readonly record struct RuntimeMethod(
        string Selector,
        IntPtr Implementation,
        string TypeEncoding);

    /// <summary>
    /// Registers a class deriving from <c>NSObject</c>, or answers the existing one when a class of
    /// that name is already registered.
    /// </summary>
    internal static IntPtr DefineClass(
        string name,
        IReadOnlyList<RuntimeMethod> methods,
        string? protocolName = null)
    {
        var existing = ObjectiveCNative.GetClass(name);
        if (existing != IntPtr.Zero)
            return existing;

        var type = objc_allocateClassPair(ObjectiveCNative.GetClass("NSObject"), name, 0);
        if (type == IntPtr.Zero)
        {
            // Another thread registered it between the lookup and the allocation.
            return ObjectiveCNative.GetClass(name) is var raced && raced != IntPtr.Zero
                ? raced
                : throw new InvalidOperationException($"The class '{name}' could not be allocated.");
        }

        if (protocolName is not null)
        {
            var protocol = objc_getProtocol(protocolName);
            if (protocol != IntPtr.Zero)
                class_addProtocol(type, protocol);
        }

        foreach (var method in methods)
        {
            if (!class_addMethod(
                    type,
                    ObjectiveCNative.GetSelector(method.Selector),
                    method.Implementation,
                    method.TypeEncoding))
            {
                throw new InvalidOperationException(
                    $"The method '{method.Selector}' could not be added to '{name}'.");
            }
        }

        objc_registerClassPair(type);
        return type;
    }

    /// <summary>Creates an instance. The caller owns it and must release it.</summary>
    internal static IntPtr CreateInstance(IntPtr type) => class_createInstance(type, 0);
}
