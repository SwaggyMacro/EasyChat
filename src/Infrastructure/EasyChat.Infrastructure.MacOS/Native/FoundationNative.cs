using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>
/// Foundation value types used to talk to AppKit. Everything here converts between managed data and
/// autoreleased Objective-C objects; no handle is cached across an autorelease pool.
/// </summary>
internal static class FoundationNative
{
    internal static IntPtr CreateString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return ObjectiveCNative.SendReturningHandle(
                ObjectiveCNative.GetClass("NSString"),
                ObjectiveCNative.GetSelector("stringWithUTF8String:"),
                utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    internal static string? ReadString(IntPtr text) => ObjectiveCNative.ReadString(text);

    internal static unsafe IntPtr CreateData(ReadOnlySpan<byte> bytes)
    {
        fixed (byte* pointer = bytes)
        {
            return ObjectiveCNative.SendReturningHandle(
                ObjectiveCNative.GetClass("NSData"),
                ObjectiveCNative.GetSelector("dataWithBytes:length:"),
                (IntPtr)pointer,
                bytes.Length);
        }
    }

    internal static byte[]? ReadData(IntPtr data)
    {
        if (data == IntPtr.Zero)
            return null;

        var length = ObjectiveCNative.SendReturningNInt(
            data,
            ObjectiveCNative.GetSelector("length"));
        if (length <= 0)
            return [];

        var bytes = ObjectiveCNative.SendReturningHandle(
            data,
            ObjectiveCNative.GetSelector("bytes"));
        if (bytes == IntPtr.Zero)
            return null;

        var buffer = new byte[length];
        Marshal.Copy(bytes, buffer, 0, (int)length);
        return buffer;
    }

    internal static nint CountOf(IntPtr array) =>
        array == IntPtr.Zero
            ? 0
            : ObjectiveCNative.SendReturningNInt(array, ObjectiveCNative.GetSelector("count"));

    internal static IntPtr ItemAt(IntPtr array, nint index) =>
        ObjectiveCNative.SendReturningHandle(
            array,
            ObjectiveCNative.GetSelector("objectAtIndex:"),
            index);

    internal static IntPtr CreateMutableArray() =>
        ObjectiveCNative.SendReturningHandle(
            ObjectiveCNative.GetClass("NSMutableArray"),
            ObjectiveCNative.GetSelector("array"));

    internal static void Add(IntPtr array, IntPtr item) =>
        ObjectiveCNative.Send(array, ObjectiveCNative.GetSelector("addObject:"), item);
}
