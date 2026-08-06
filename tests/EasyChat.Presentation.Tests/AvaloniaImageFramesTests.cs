using System.Runtime.InteropServices;
using EasyChat.Contracts.Platform;
using EasyChat.Presentation.ImageTranslation;

namespace EasyChat.Presentation.Tests;

[TestClass]
public sealed class AvaloniaImageFramesTests
{
    [TestMethod]
    public void CopyToFramebuffer_PreservesRowsAndSkipsSourceAndDestinationPadding()
    {
        var backing = new byte[]
        {
            99, 99,
            1, 2, 3, 4, 5, 6, 7, 8, 90, 91, 92, 93,
            9, 10, 11, 12, 13, 14, 15, 16, 94, 95, 96, 97,
            99, 99
        };
        var frame = new ImageFrame(
            2,
            2,
            12,
            96,
            96,
            backing.AsMemory(2, 24));
        var destination = Enumerable.Repeat((byte)0xCC, 20).ToArray();
        var pinned = GCHandle.Alloc(destination, GCHandleType.Pinned);
        try
        {
            AvaloniaImageFrames.CopyToFramebuffer(
                frame,
                pinned.AddrOfPinnedObject(),
                destinationRowBytes: 10);
        }
        finally
        {
            pinned.Free();
        }

        CollectionAssert.AreEqual(
            new byte[]
            {
                1, 2, 3, 4, 5, 6, 7, 8, 0xCC, 0xCC,
                9, 10, 11, 12, 13, 14, 15, 16, 0xCC, 0xCC
            },
            destination);
    }
}
