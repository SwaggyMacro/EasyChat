using System.Buffers;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EasyChat.Contracts.Platform;

namespace EasyChat.Presentation.ImageTranslation;

public static class AvaloniaImageFrames
{
    public static Bitmap ToBitmap(ImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.PixelFormat != ImagePixelFormat.Bgra32)
            throw new NotSupportedException($"Pixel format '{frame.PixelFormat}' is not supported.");

        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(frame.DpiX, frame.DpiY),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using var locked = bitmap.Lock();
        CopyToFramebuffer(frame, locked.Address, locked.RowBytes);

        return bitmap;
    }

    internal static void CopyToFramebuffer(
        ImageFrame frame,
        IntPtr destination,
        int destinationRowBytes)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (destination == IntPtr.Zero)
            throw new ArgumentException("The destination framebuffer address is invalid.", nameof(destination));
        var rowBytes = checked(frame.Width * 4);
        if (destinationRowBytes < rowBytes)
            throw new ArgumentOutOfRangeException(nameof(destinationRowBytes));

        if (MemoryMarshal.TryGetArray(frame.Pixels, out var sourceBuffer)
            && sourceBuffer.Array is not null)
        {
            for (var row = 0; row < frame.Height; row++)
            {
                Marshal.Copy(
                    sourceBuffer.Array,
                    sourceBuffer.Offset + row * frame.Stride,
                    destination + row * destinationRowBytes,
                    rowBytes);
            }
        }
        else
        {
            var source = frame.Pixels.Span;
            var rowBuffer = ArrayPool<byte>.Shared.Rent(rowBytes);
            try
            {
                for (var row = 0; row < frame.Height; row++)
                {
                    source.Slice(row * frame.Stride, rowBytes).CopyTo(rowBuffer);
                    Marshal.Copy(
                        rowBuffer,
                        0,
                        destination + row * destinationRowBytes,
                        rowBytes);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rowBuffer);
            }
        }
    }

    public static ImageFrame ToImageFrame(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var pixelSize = bitmap.PixelSize;
        var dpiX = bitmap.Dpi.X > 0 ? bitmap.Dpi.X : 96d;
        var dpiY = bitmap.Dpi.Y > 0 ? bitmap.Dpi.Y : 96d;
        var stride = checked(pixelSize.Width * 4);
        var pixels = GC.AllocateUninitializedArray<byte>(checked(stride * pixelSize.Height));
        using (var framebuffer = new PinnedFramebuffer(
                   pixels,
                   pixelSize,
                   stride,
                   new Vector(dpiX, dpiY)))
        {
            bitmap.CopyPixels(framebuffer);
        }

        return new ImageFrame(
            pixelSize.Width,
            pixelSize.Height,
            stride,
            dpiX,
            dpiY,
            pixels,
            ImagePixelFormat.Bgra32);
    }

    private sealed class PinnedFramebuffer : ILockedFramebuffer
    {
        private GCHandle _pixels;

        internal PinnedFramebuffer(
            byte[] pixels,
            PixelSize size,
            int rowBytes,
            Vector dpi)
        {
            ArgumentNullException.ThrowIfNull(pixels);
            _pixels = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            Address = _pixels.AddrOfPinnedObject();
            Size = size;
            RowBytes = rowBytes;
            Dpi = dpi;
        }

        public IntPtr Address { get; }
        public PixelSize Size { get; }
        public int RowBytes { get; }
        public Vector Dpi { get; }
        public PixelFormat Format => PixelFormat.Bgra8888;
        public AlphaFormat AlphaFormat => AlphaFormat.Opaque;

        public void Dispose()
        {
            if (_pixels.IsAllocated)
                _pixels.Free();
        }
    }
}
