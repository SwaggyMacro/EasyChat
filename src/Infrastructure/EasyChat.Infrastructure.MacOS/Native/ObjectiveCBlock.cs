using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>
/// Layout of a global Objective-C block literal. A global block is never copied or freed by
/// libclosure, so a statically allocated instance stays valid for the lifetime of the process and
/// needs no reference counting from managed code.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ObjectiveCBlock
{
    /// <summary>Value of <c>BLOCK_IS_GLOBAL</c>.</summary>
    internal const int GlobalFlag = 1 << 28;

    internal IntPtr Isa;
    internal int Flags;
    internal int Reserved;
    internal IntPtr Invoke;
    internal IntPtr Descriptor;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ObjectiveCBlockDescriptor
{
    internal nuint Reserved;
    internal nuint Size;
}

internal static class ObjectiveCBlockFactory
{
    private const string SystemLibraryPath = "/usr/lib/libSystem.B.dylib";

    /// <summary>
    /// Allocates a never-released global block whose invoke function is <paramref name="invoke"/>.
    /// The returned pointer is the block reference that Objective-C APIs expect.
    /// </summary>
    internal static unsafe IntPtr CreateGlobalBlock(IntPtr invoke)
    {
        var descriptor = (ObjectiveCBlockDescriptor*)NativeMemory.AllocZeroed(
            (nuint)sizeof(ObjectiveCBlockDescriptor));
        descriptor->Reserved = 0;
        descriptor->Size = (nuint)sizeof(ObjectiveCBlock);

        var block = (ObjectiveCBlock*)NativeMemory.AllocZeroed((nuint)sizeof(ObjectiveCBlock));
        // _NSConcreteGlobalBlock is an array symbol, so its export address is the isa itself and
        // must not be dereferenced the way a pointer-typed constant such as kCFBooleanTrue is.
        block->Isa = NativeLibrary.GetExport(
            NativeLibrary.Load(SystemLibraryPath),
            "_NSConcreteGlobalBlock");
        block->Flags = ObjectiveCBlock.GlobalFlag;
        block->Reserved = 0;
        block->Invoke = invoke;
        block->Descriptor = (IntPtr)descriptor;
        return (IntPtr)block;
    }
}
