using EasyChat.Contracts.Capture;
using EasyChat.Contracts.Platform;

namespace EasyChat.Desktop.Windows.Capture;

internal static class ScreenshotWorkerProtocol
{
    private const int Magic = 0x50414353;
    private const int Version = 3;
    private const int MaxImageBytes = 128 * 1024 * 1024;

    internal static void WriteReady(BinaryWriter writer)
    {
        WriteHeader(writer, ScreenshotWorkerMessage.Ready);
        writer.Flush();
    }

    internal static void ReadReady(BinaryReader reader)
    {
        var message = ReadHeader(reader);
        if (message == ScreenshotWorkerMessage.Failed)
            throw new InvalidOperationException($"Screenshot worker failed: {reader.ReadString()}");
        if (message != ScreenshotWorkerMessage.Ready)
            throw new InvalidDataException("Screenshot worker did not send a ready response.");
    }

    internal static void WriteRequest(BinaryWriter writer, ScreenshotWorkerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.DefaultAction) || !Enum.IsDefined(request.ToolbarMode))
            throw new InvalidDataException("Screenshot worker request options are invalid.");
        WriteHeader(writer, ScreenshotWorkerMessage.Request);
        writer.Write(request.Precise);
        writer.Write(request.Theme);
        writer.Write(request.CultureName);
        writer.Write((int)request.DefaultAction);
        writer.Write((int)request.ToolbarMode);
        writer.Flush();
    }

    internal static ScreenshotWorkerRequest ReadRequest(BinaryReader reader)
    {
        if (ReadHeader(reader) != ScreenshotWorkerMessage.Request)
            throw new InvalidDataException("Screenshot worker request is invalid.");
        var request = new ScreenshotWorkerRequest(
            reader.ReadBoolean(),
            reader.ReadString(),
            reader.ReadString(),
            (CaptureOverlayAction)reader.ReadInt32(),
            (CaptureToolbarMode)reader.ReadInt32());
        if (!Enum.IsDefined(request.DefaultAction) || !Enum.IsDefined(request.ToolbarMode))
            throw new InvalidDataException("Screenshot worker request options are invalid.");
        return request;
    }

    internal static void WriteCancelled(BinaryWriter writer)
    {
        WriteHeader(writer, ScreenshotWorkerMessage.Cancelled);
        writer.Flush();
    }

    internal static void WriteSuccess(BinaryWriter writer, ScreenshotSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!Enum.IsDefined(selection.Action))
            throw new InvalidDataException("Screenshot worker action is invalid.");
        WriteHeader(writer, ScreenshotWorkerMessage.Success);
        writer.Write((int)selection.Action);
        writer.Write(selection.CompletionPoint.X);
        writer.Write(selection.CompletionPoint.Y);
        WriteImage(writer, selection.Image);
        writer.Flush();
    }

    internal static void WriteFailure(BinaryWriter writer, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        WriteHeader(writer, ScreenshotWorkerMessage.Failed);
        writer.Write(exception.Message);
        writer.Flush();
    }

    internal static ScreenshotSelection? Read(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var message = ReadHeader(reader);
        return message switch
        {
            ScreenshotWorkerMessage.Cancelled => null,
            ScreenshotWorkerMessage.Success => ReadSuccess(reader),
            ScreenshotWorkerMessage.Failed => throw new InvalidOperationException(
                $"Screenshot worker failed: {reader.ReadString()}"),
            _ => throw new InvalidDataException("Screenshot worker status is invalid.")
        };
    }

    private static ScreenshotSelection ReadSuccess(BinaryReader reader)
    {
        var action = (CaptureOverlayAction)reader.ReadInt32();
        if (!Enum.IsDefined(action))
            throw new InvalidDataException("Screenshot worker action is invalid.");
        var completionPoint = new PhysicalScreenPoint(reader.ReadInt32(), reader.ReadInt32());
        return new ScreenshotSelection(ReadImage(reader), action, completionPoint);
    }

    private static void WriteHeader(BinaryWriter writer, ScreenshotWorkerMessage message)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write((byte)message);
    }

    private static ScreenshotWorkerMessage ReadHeader(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version)
            throw new InvalidDataException("Screenshot worker protocol header is invalid.");
        var message = (ScreenshotWorkerMessage)reader.ReadByte();
        return Enum.IsDefined(message)
            ? message
            : throw new InvalidDataException("Screenshot worker message is invalid.");
    }

    private static void WriteImage(BinaryWriter writer, ImageFrame image)
    {
        if (image.PixelFormat != ImagePixelFormat.Bgra32)
            throw new NotSupportedException($"Pixel format '{image.PixelFormat}' is not supported.");

        var length = checked(image.Stride * image.Height);
        if (length > MaxImageBytes)
            throw new InvalidDataException("Screenshot worker image buffer is too large.");

        writer.Write(image.Width);
        writer.Write(image.Height);
        writer.Write(image.Stride);
        writer.Write(image.DpiX);
        writer.Write(image.DpiY);
        writer.Write((int)image.PixelFormat);
        writer.Write(length);
        var pixels = image.Pixels.Span;
        for (var row = 0; row < image.Height; row++)
            writer.Write(pixels.Slice(row * image.Stride, image.Stride));
    }

    private static ImageFrame ReadImage(BinaryReader reader)
    {
        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        var stride = reader.ReadInt32();
        var dpiX = reader.ReadDouble();
        var dpiY = reader.ReadDouble();
        var pixelFormat = (ImagePixelFormat)reader.ReadInt32();
        if (!Enum.IsDefined(pixelFormat) || pixelFormat != ImagePixelFormat.Bgra32)
            throw new InvalidDataException("Screenshot worker pixel format is invalid.");
        if (width <= 0 || height <= 0 || stride < checked(width * 4))
            throw new InvalidDataException("Screenshot worker image dimensions are invalid.");
        if (dpiX <= 0 || dpiY <= 0)
            throw new InvalidDataException("Screenshot worker image DPI is invalid.");

        var expectedLength = checked(stride * height);
        var length = reader.ReadInt32();
        if (length != expectedLength || length > MaxImageBytes)
            throw new InvalidDataException("Screenshot worker image buffer length is invalid.");

        var pixels = GC.AllocateUninitializedArray<byte>(length);
        var offset = 0;
        while (offset < length)
        {
            var read = reader.Read(pixels, offset, length - offset);
            if (read == 0)
                throw new EndOfStreamException("Screenshot worker returned an incomplete image.");
            offset += read;
        }

        return new ImageFrame(width, height, stride, dpiX, dpiY, pixels, pixelFormat);
    }

    private enum ScreenshotWorkerMessage : byte
    {
        Ready = 0,
        Request = 1,
        Success = 2,
        Cancelled = 3,
        Failed = 4
    }
}

internal sealed record ScreenshotWorkerRequest(
    bool Precise,
    string Theme,
    string CultureName,
    CaptureOverlayAction DefaultAction,
    CaptureToolbarMode ToolbarMode);
