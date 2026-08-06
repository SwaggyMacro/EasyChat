using System.Buffers;
using System.Runtime.InteropServices;
using EasyChat.Contracts.Platform;
using EasyChat.Shared.Results;

namespace EasyChat.Infrastructure.Windows.Input;

public sealed class WindowsClipboardImage : IClipboardImage
{
    private const uint Dib = 8;
    private const uint MoveableMemory = 0x0002;
    private const int BitmapInfoHeaderSize = 40;

    public ValueTask<Result> WriteAsync(
        ImageFrame image,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        if (image.PixelFormat != ImagePixelFormat.Bgra32)
        {
            return ValueTask.FromResult(Result.Failure(new Error(
                "clipboard.image-format-unsupported",
                $"Pixel format '{image.PixelFormat}' is not supported.")));
        }

        try
        {
            if (!OpenClipboardWithRetry())
            {
                return ValueTask.FromResult(Result.Failure(new Error(
                    "clipboard.open-failed",
                    "The Windows clipboard could not be opened.")));
            }

            IntPtr memory = IntPtr.Zero;
            try
            {
                if (!EmptyClipboard())
                    throw new InvalidOperationException("The Windows clipboard could not be emptied.");

                var rowBytes = checked(image.Width * 4);
                var pixelBytes = checked(rowBytes * image.Height);
                var totalBytes = checked(BitmapInfoHeaderSize + pixelBytes);
                memory = GlobalAlloc(MoveableMemory, new UIntPtr((uint)totalBytes));
                if (memory == IntPtr.Zero)
                    throw new OutOfMemoryException("Unable to allocate clipboard image memory.");

                var destination = GlobalLock(memory);
                if (destination == IntPtr.Zero)
                    throw new OutOfMemoryException("Unable to lock clipboard image memory.");
                try
                {
                    WriteHeader(destination, image, pixelBytes);
                    var source = image.Pixels.Span;
                    var row = ArrayPool<byte>.Shared.Rent(rowBytes);
                    try
                    {
                        for (var destinationRow = 0; destinationRow < image.Height; destinationRow++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var sourceRow = image.Height - destinationRow - 1;
                            source.Slice(sourceRow * image.Stride, rowBytes).CopyTo(row);
                            Marshal.Copy(
                                row,
                                0,
                                destination + BitmapInfoHeaderSize + destinationRow * rowBytes,
                                rowBytes);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(row);
                    }
                }
                finally
                {
                    GlobalUnlock(memory);
                }

                if (SetClipboardData(Dib, memory) == IntPtr.Zero)
                    throw new InvalidOperationException("Unable to set the Windows clipboard image.");

                memory = IntPtr.Zero;
                return ValueTask.FromResult(Result.Success());
            }
            finally
            {
                if (memory != IntPtr.Zero)
                    GlobalFree(memory);
                CloseClipboard();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ValueTask.FromResult(Result.Failure(new Error(
                "clipboard.image-write-failed",
                exception.Message)));
        }
    }

    private static void WriteHeader(IntPtr target, ImageFrame image, int pixelBytes)
    {
        Marshal.WriteInt32(target, 0, BitmapInfoHeaderSize);
        Marshal.WriteInt32(target, 4, image.Width);
        Marshal.WriteInt32(target, 8, image.Height);
        Marshal.WriteInt16(target, 12, 1);
        Marshal.WriteInt16(target, 14, 32);
        Marshal.WriteInt32(target, 16, 0);
        Marshal.WriteInt32(target, 20, pixelBytes);
        Marshal.WriteInt32(target, 24, ToPixelsPerMeter(image.DpiX));
        Marshal.WriteInt32(target, 28, ToPixelsPerMeter(image.DpiY));
        Marshal.WriteInt32(target, 32, 0);
        Marshal.WriteInt32(target, 36, 0);
    }

    private static int ToPixelsPerMeter(double dpi) =>
        dpi > 0 && double.IsFinite(dpi)
            ? checked((int)Math.Round(dpi / 0.0254))
            : 3780;

    private static bool OpenClipboardWithRetry()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
                return true;
            Thread.Sleep(10);
        }
        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);
}
