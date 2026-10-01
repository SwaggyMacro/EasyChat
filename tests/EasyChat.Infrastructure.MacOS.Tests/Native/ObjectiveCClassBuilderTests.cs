using System.Runtime.InteropServices;
using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS.Tests.Native;

/// <summary>
/// Proves a class defined at runtime is a class the Objective-C runtime will actually dispatch to.
/// </summary>
/// <remarks>
/// Some Apple APIs only deliver results to a delegate object, with no block alternative, so the
/// alternative to this machinery is not having those callbacks at all. A wrong type encoding
/// corrupts the call frame rather than failing cleanly, which is why the arguments are asserted here
/// and not only the fact that the method ran.
/// </remarks>
[TestClass]
public sealed class ObjectiveCClassBuilderTests
{
    private static int _invocations;
    private static IntPtr _lastObject;
    private static long _lastNumber;

    [TestMethod]
    public void AClassDefinedAtRuntimeReceivesRealMessages()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            Assert.Inconclusive("The Objective-C runtime can only be verified on macOS 26 or later.");
            return;
        }

        using var pool = AutoreleasePool.Push();
        _invocations = 0;
        _lastObject = IntPtr.Zero;
        _lastNumber = 0;

        var type = ObjectiveCClassBuilder.DefineClass(
            "EasyChatRuntimeCallbackProbe",
            [
                new ObjectiveCClassBuilder.RuntimeMethod(
                    "handleObject:withNumber:",
                    CallbackPointer,
                    // void return, self, _cmd, one object, one long.
                    "v@:@q")
            ]);
        Assert.AreNotEqual(IntPtr.Zero, type);

        var instance = ObjectiveCClassBuilder.CreateInstance(type);
        Assert.AreNotEqual(IntPtr.Zero, instance);

        var argument = FoundationNative.CreateString("payload");
        ObjectiveCNative.Send(
            instance,
            ObjectiveCNative.GetSelector("handleObject:withNumber:"),
            argument,
            42);

        Assert.AreEqual(1, _invocations, "The runtime should have dispatched to the managed method.");
        Assert.AreEqual(argument, _lastObject, "The object argument must arrive intact.");
        Assert.AreEqual(42, _lastNumber, "The integer argument must arrive intact.");
    }

    [TestMethod]
    public void DefiningTheSameClassTwiceAnswersTheOneAlreadyRegistered()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            Assert.Inconclusive("The Objective-C runtime can only be verified on macOS 26 or later.");
            return;
        }

        // A class name can only be registered once per process, so building it again has to be
        // harmless: the adapters define theirs lazily and may be constructed more than once.
        var first = ObjectiveCClassBuilder.DefineClass(
            "EasyChatRuntimeIdempotenceProbe",
            [
                new ObjectiveCClassBuilder.RuntimeMethod("handleObject:withNumber:", CallbackPointer, "v@:@q")
            ]);
        var second = ObjectiveCClassBuilder.DefineClass(
            "EasyChatRuntimeIdempotenceProbe",
            [
                new ObjectiveCClassBuilder.RuntimeMethod("handleObject:withNumber:", CallbackPointer, "v@:@q")
            ]);

        Assert.AreEqual(first, second);
    }

    private static unsafe IntPtr CallbackPointer =>
        (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, long, void>)&OnMessage;

    /// <summary>Matches the <c>v@:@q</c> encoding: self, _cmd, an object and a long.</summary>
    [UnmanagedCallersOnly]
    private static void OnMessage(IntPtr self, IntPtr selector, IntPtr argument, long number)
    {
        _invocations++;
        _lastObject = argument;
        _lastNumber = number;
    }
}
