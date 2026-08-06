using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.Windows.Workers;

internal static class WindowsImageFrameProtocol
{
    private const int MaxImageBytes = 512 * 1024 * 1024;

    internal static void Write(BinaryWriter writer, ImageFrame frame)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.PixelFormat != ImagePixelFormat.Bgra32)
            throw new NotSupportedException($"Pixel format '{frame.PixelFormat}' is not supported.");

        var length = checked(frame.Stride * frame.Height);
        if (length > MaxImageBytes)
            throw new InvalidDataException("Worker image buffer is too large.");

        writer.Write(frame.Width);
        writer.Write(frame.Height);
        writer.Write(frame.Stride);
        writer.Write(frame.DpiX);
        writer.Write(frame.DpiY);
        writer.Write((int)frame.PixelFormat);
        writer.Write(length);

        var pixels = frame.Pixels.Span;
        for (var row = 0; row < frame.Height; row++)
            writer.Write(pixels.Slice(row * frame.Stride, frame.Stride));
    }

    internal static ImageFrame Read(BinaryReader reader)
    {
        byte[]? reusableBuffer = null;
        return Read(reader, ref reusableBuffer);
    }

    internal static ImageFrame Read(BinaryReader reader, ref byte[]? reusableBuffer)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        var stride = reader.ReadInt32();
        var dpiX = reader.ReadDouble();
        var dpiY = reader.ReadDouble();
        var pixelFormat = (ImagePixelFormat)reader.ReadInt32();
        if (!Enum.IsDefined(pixelFormat))
            throw new InvalidDataException("Worker image pixel format is invalid.");
        if (width <= 0 || height <= 0 || stride < checked(width * 4))
            throw new InvalidDataException("Worker image dimensions are invalid.");
        if (dpiX <= 0 || dpiY <= 0)
            throw new InvalidDataException("Worker image DPI is invalid.");

        var expectedLength = checked(stride * height);
        var length = reader.ReadInt32();
        if (length != expectedLength || length > MaxImageBytes)
            throw new InvalidDataException("Worker image buffer length is invalid.");
        if (reusableBuffer is null || reusableBuffer.Length < length)
            reusableBuffer = GC.AllocateUninitializedArray<byte>(length);
        var offset = 0;
        while (offset < length)
        {
            var read = reader.Read(reusableBuffer, offset, length - offset);
            if (read == 0)
                throw new EndOfStreamException("Worker received an incomplete image buffer.");
            offset += read;
        }

        return new ImageFrame(
            width,
            height,
            stride,
            dpiX,
            dpiY,
            reusableBuffer.AsMemory(0, length),
            pixelFormat);
    }
}
