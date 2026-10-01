using EasyChat.Infrastructure.MacOS.Native;

namespace EasyChat.Infrastructure.MacOS.Tests.Capture;

/// <summary>
/// Pixel-level checks on the CoreGraphics bridge. Channel order, row direction and stride are where
/// a capture goes subtly wrong — a flipped image or swapped red and blue still looks like a
/// screenshot, and only OCR quality would betray it — so they are asserted against known bytes
/// rather than eyeballed.
/// </summary>
[TestClass]
public sealed class ImageEncodingNativeTests
{
    private const int Width = 4;
    private const int Height = 3;
    private const int Stride = Width * 4;

    private static bool Skip()
    {
        if (OperatingSystem.IsMacOSVersionAtLeast(26))
            return false;

        Assert.Inconclusive("CoreGraphics can only be verified on macOS 26 or later.");
        return true;
    }

    [TestMethod]
    public void PixelsSurviveTheRoundTripThroughCoreGraphicsUnchanged()
    {
        if (Skip())
            return;

        var original = CreateDistinctPixels();
        var image = ImageEncodingNative.CreateImage(original, Width, Height, Stride);
        Assert.AreNotEqual(IntPtr.Zero, image);

        try
        {
            var read = ImageEncodingNative.ReadBgra32(
                image,
                out var width,
                out var height,
                out var stride);

            Assert.AreEqual(Width, width);
            Assert.AreEqual(Height, height);
            Assert.AreEqual(Stride, stride);
            CollectionAssert.AreEqual(
                original,
                read,
                "Blue, green, red and alpha must come back in the same order and the same rows.");
        }
        finally
        {
            ImageEncodingNative.CGImageRelease(image);
        }
    }

    [TestMethod]
    public void TheFirstRowInMemoryIsTheTopOfTheImage()
    {
        if (Skip())
            return;

        var original = CreateDistinctPixels();
        var image = ImageEncodingNative.CreateImage(original, Width, Height, Stride);
        try
        {
            var read = ImageEncodingNative.ReadBgra32(image, out _, out _, out var stride);

            // Each row was filled with its own index, so a vertical flip would put row 2 first.
            Assert.AreEqual(0, read[0 * stride + 2], "row 0 red channel");
            Assert.AreEqual(2, read[2 * stride + 2], "row 2 red channel");
        }
        finally
        {
            ImageEncodingNative.CGImageRelease(image);
        }
    }

    [TestMethod]
    public void AnImageWithPaddedRowsIsReadBackTightlyPacked()
    {
        if (Skip())
            return;

        // A captured frame often has rows padded to an alignment boundary; the contract's frame is
        // tightly packed, so the padding must be dropped rather than carried through as pixels.
        const int paddedStride = Stride + 16;
        var padded = new byte[paddedStride * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var offset = (y * paddedStride) + (x * 4);
                padded[offset] = (byte)(x + 1);
                padded[offset + 1] = (byte)(y + 1);
                padded[offset + 2] = (byte)y;
                padded[offset + 3] = 0xFF;
            }
        }

        var image = ImageEncodingNative.CreateImage(padded, Width, Height, paddedStride);
        try
        {
            var read = ImageEncodingNative.ReadBgra32(image, out _, out _, out var stride);

            Assert.AreEqual(Stride, stride);
            Assert.HasCount(Stride * Height, read);
            Assert.AreEqual(1, read[0], "first pixel blue");
            Assert.AreEqual(Width, read[((Width - 1) * 4) + 0], "last pixel of row 0 blue");
        }
        finally
        {
            ImageEncodingNative.CGImageRelease(image);
        }
    }

    [TestMethod]
    public void AnEncodedFrameIsARealPngOfTheRequestedSize()
    {
        if (Skip())
            return;

        var png = ImageEncodingNative.EncodePng(CreateDistinctPixels(), Width, Height, Stride);

        Assert.IsGreaterThan(8, png.Length);
        CollectionAssert.AreEqual(
            new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' },
            png[..4]);
    }

    /// <summary>Every pixel differs, so a swapped channel or a flipped row cannot go unnoticed.</summary>
    private static byte[] CreateDistinctPixels()
    {
        var pixels = new byte[Stride * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var offset = (y * Stride) + (x * 4);
                pixels[offset] = (byte)(x + 1);
                pixels[offset + 1] = (byte)(y + 1);
                pixels[offset + 2] = (byte)y;
                pixels[offset + 3] = 0xFF;
            }
        }

        return pixels;
    }
}
