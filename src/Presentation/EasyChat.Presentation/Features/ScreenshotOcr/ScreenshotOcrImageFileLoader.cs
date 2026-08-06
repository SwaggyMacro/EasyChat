using Avalonia.Media.Imaging;
using EasyChat.Contracts.Platform;
using EasyChat.Presentation.ImageTranslation;
using EasyChat.Shared.Results;

namespace EasyChat.Presentation.Features.ScreenshotOcr;

internal static class ScreenshotOcrImageFileLoader
{
    private const long MaximumEncodedBytes = 256L * 1024 * 1024;

    internal static Task<ImageFrame> LoadAsync(
        string path,
        Func<int, int, Result> validateDimensions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(validateDimensions);
        return Task.Run(() => Load(path, validateDimensions, cancellationToken), cancellationToken);
    }

    private static ImageFrame Load(
        string path,
        Func<int, int, Result> validateDimensions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp"))
            throw new InvalidDataException("Supported image formats are PNG, JPEG, BMP, and WebP.");

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        if (stream.Length <= 0 || stream.Length > MaximumEncodedBytes)
            throw new InvalidDataException("The encoded image file is empty or exceeds 256 MiB.");

        var dimensions = ReadDimensions(stream, extension);
        var validation = validateDimensions(dimensions.Width, dimensions.Height);
        if (validation.IsFailure)
            throw new InvalidDataException(validation.Error.Message);

        cancellationToken.ThrowIfCancellationRequested();
        stream.Position = 0;
        using var bitmap = new Bitmap(stream);
        var decodedValidation = validateDimensions(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        if (decodedValidation.IsFailure)
            throw new InvalidDataException(decodedValidation.Error.Message);
        if (bitmap.PixelSize.Width != dimensions.Width || bitmap.PixelSize.Height != dimensions.Height)
            throw new InvalidDataException("The decoded image dimensions do not match its header.");
        cancellationToken.ThrowIfCancellationRequested();
        return AvaloniaImageFrames.ToImageFrame(bitmap);
    }

    private static PixelDimensions ReadDimensions(Stream stream, string extension)
    {
        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var dimensions = extension switch
        {
            ".png" => ReadPng(reader),
            ".jpg" or ".jpeg" => ReadJpeg(reader),
            ".bmp" => ReadBmp(reader),
            ".webp" => ReadWebP(reader),
            _ => throw new InvalidDataException("The image format is not supported.")
        };
        if (dimensions.Width <= 0 || dimensions.Height <= 0)
            throw new InvalidDataException("The image dimensions are invalid.");
        return dimensions;
    }

    private static PixelDimensions ReadPng(BinaryReader reader)
    {
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (!reader.ReadBytes(8).SequenceEqual(signature))
            throw new InvalidDataException("The PNG signature is invalid.");
        var chunkLength = ReadBigEndianInt32(reader);
        if (chunkLength != 13 || new string(reader.ReadChars(4)) != "IHDR")
            throw new InvalidDataException("The PNG header is invalid.");
        return new PixelDimensions(ReadBigEndianInt32(reader), ReadBigEndianInt32(reader));
    }

    private static PixelDimensions ReadBmp(BinaryReader reader)
    {
        if (reader.ReadByte() != (byte)'B' || reader.ReadByte() != (byte)'M')
            throw new InvalidDataException("The BMP signature is invalid.");
        reader.BaseStream.Position = 18;
        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        if (height == int.MinValue)
            throw new InvalidDataException("The BMP height is invalid.");
        return new PixelDimensions(width, Math.Abs(height));
    }

    private static PixelDimensions ReadJpeg(BinaryReader reader)
    {
        if (reader.ReadByte() != 0xff || reader.ReadByte() != 0xd8)
            throw new InvalidDataException("The JPEG signature is invalid.");

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            byte markerPrefix;
            do markerPrefix = reader.ReadByte(); while (markerPrefix != 0xff);
            byte marker;
            do marker = reader.ReadByte(); while (marker == 0xff);
            if (marker is 0xd8 or 0xd9 || marker is >= 0xd0 and <= 0xd7)
                continue;
            var length = ReadBigEndianUInt16(reader);
            if (length < 2)
                throw new InvalidDataException("The JPEG segment length is invalid.");
            if (IsStartOfFrame(marker))
            {
                _ = reader.ReadByte();
                var height = ReadBigEndianUInt16(reader);
                var width = ReadBigEndianUInt16(reader);
                return new PixelDimensions(width, height);
            }
            reader.BaseStream.Seek(length - 2, SeekOrigin.Current);
        }
        throw new InvalidDataException("The JPEG dimensions were not found.");
    }

    private static PixelDimensions ReadWebP(BinaryReader reader)
    {
        if (new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("The WebP RIFF header is invalid.");
        _ = reader.ReadUInt32();
        if (new string(reader.ReadChars(4)) != "WEBP")
            throw new InvalidDataException("The WebP signature is invalid.");
        var chunk = new string(reader.ReadChars(4));
        var length = reader.ReadUInt32();
        if (length > 64 * 1024 * 1024)
            throw new InvalidDataException("The WebP header chunk is invalid.");

        return chunk switch
        {
            "VP8X" => ReadWebPExtended(reader, length),
            "VP8 " => ReadWebPLossy(reader, length),
            "VP8L" => ReadWebPLossless(reader, length),
            _ => throw new InvalidDataException("The WebP image chunk is not supported.")
        };
    }

    private static PixelDimensions ReadWebPExtended(BinaryReader reader, uint length)
    {
        if (length < 10)
            throw new InvalidDataException("The WebP extended header is incomplete.");
        _ = reader.ReadBytes(4);
        var width = 1 + ReadUInt24(reader);
        var height = 1 + ReadUInt24(reader);
        return new PixelDimensions(width, height);
    }

    private static PixelDimensions ReadWebPLossy(BinaryReader reader, uint length)
    {
        if (length < 10)
            throw new InvalidDataException("The WebP VP8 header is incomplete.");
        _ = reader.ReadBytes(3);
        if (!reader.ReadBytes(3).SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a }))
            throw new InvalidDataException("The WebP VP8 frame header is invalid.");
        var width = reader.ReadUInt16() & 0x3fff;
        var height = reader.ReadUInt16() & 0x3fff;
        return new PixelDimensions(width, height);
    }

    private static PixelDimensions ReadWebPLossless(BinaryReader reader, uint length)
    {
        if (length < 5 || reader.ReadByte() != 0x2f)
            throw new InvalidDataException("The WebP lossless header is invalid.");
        var first = reader.ReadByte();
        var second = reader.ReadByte();
        var third = reader.ReadByte();
        var fourth = reader.ReadByte();
        var width = 1 + first + ((second & 0x3f) << 8);
        var height = 1 + (second >> 6) + (third << 2) + ((fourth & 0x0f) << 10);
        return new PixelDimensions(width, height);
    }

    private static bool IsStartOfFrame(byte marker) => marker is
        0xc0 or 0xc1 or 0xc2 or 0xc3 or 0xc5 or 0xc6 or 0xc7
        or 0xc9 or 0xca or 0xcb or 0xcd or 0xce or 0xcf;

    private static int ReadBigEndianInt32(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (bytes.Length != 4)
            throw new EndOfStreamException();
        return checked((int)((uint)bytes[0] << 24
                             | (uint)bytes[1] << 16
                             | (uint)bytes[2] << 8
                             | bytes[3]));
    }

    private static ushort ReadBigEndianUInt16(BinaryReader reader)
    {
        var high = reader.ReadByte();
        var low = reader.ReadByte();
        return (ushort)((high << 8) | low);
    }

    private static int ReadUInt24(BinaryReader reader) =>
        reader.ReadByte() | reader.ReadByte() << 8 | reader.ReadByte() << 16;

    private readonly record struct PixelDimensions(int Width, int Height);
}
