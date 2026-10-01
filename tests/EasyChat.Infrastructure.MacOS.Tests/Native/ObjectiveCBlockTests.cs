using System.Runtime.InteropServices;
using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS.Tests.Native;

/// <summary>
/// Proves the hand-built global block is a block the Objective-C runtime will actually call, not
/// merely one whose header looks right. Every asynchronous Apple API EasyChat uses — microphone
/// authorisation, ScreenCaptureKit — delivers its answer through one of these, so a wrong isa,
/// flags or descriptor would fail only at runtime and only on a permission-gated path.
/// </summary>
[TestClass]
public sealed class ObjectiveCBlockTests
{
    private static int _invocations;
    private static nint _lastIndex;

    [TestMethod]
    public void AGlobalBlockIsInvokedByTheObjectiveCRuntime()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
        {
            Assert.Inconclusive("Blocks can only be verified on macOS 26 or later.");
            return;
        }

        using var pool = AutoreleasePool.Push();
        _invocations = 0;
        _lastIndex = -1;

        // NSArray's enumerator takes a block and calls it once per element, which exercises the
        // block end to end without needing any privacy approval.
        var array = FoundationNative.CreateMutableArray();
        FoundationNative.Add(array, FoundationNative.CreateString("first"));
        FoundationNative.Add(array, FoundationNative.CreateString("second"));
        FoundationNative.Add(array, FoundationNative.CreateString("third"));

        ObjectiveCNative.Send(
            array,
            ObjectiveCNative.GetSelector("enumerateObjectsUsingBlock:"),
            CreateEnumerationBlock());

        Assert.AreEqual(3, _invocations, "The runtime should have called the block once per item.");
        Assert.AreEqual(2, _lastIndex, "The block should receive the element index.");
    }

    private static unsafe IntPtr CreateEnumerationBlock() =>
        ObjectiveCBlockFactory.CreateGlobalBlock(
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, nint, IntPtr, void>)&OnElement);

    /// <summary>Matches <c>void (^)(id object, NSUInteger index, BOOL *stop)</c>.</summary>
    [UnmanagedCallersOnly]
    private static void OnElement(IntPtr block, IntPtr element, nint index, IntPtr stop)
    {
        _invocations++;
        _lastIndex = index;
    }
}
