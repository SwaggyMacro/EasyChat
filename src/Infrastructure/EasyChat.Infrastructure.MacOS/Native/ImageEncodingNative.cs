using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>
/// Encodes a BGRA32 frame as PNG with the system codecs. CoreGraphics and ImageIO are plain C, so no
/// Objective-C message send is involved, and the resulting <c>CFData</c> is toll-free bridged to
/// <c>NSData</c> for the pasteboard.
/// </summary>
internal static partial class ImageEncodingNative
{
    private const string CoreGraphicsPath =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    private const string ImageIoPath = "/System/Library/Frameworks/ImageIO.framework/ImageIO";

    /// <summary>
    /// <c>kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little</c>: a 32-bit little-endian
    /// word of ARGB is B, G, R, A in memory, which is exactly the contract's BGRA32 layout.
    /// </summary>
    private const uint Bgra32BitmapInfo = 2u | (2u << 12);

    private const int BitsPerComponent = 8;
    private const int BitsPerPixel = 32;

    /// <summary>Value of <c>kCGRenderingIntentDefault</c>.</summary>
    private const int DefaultRenderingIntent = 0;

    [LibraryImport(CoreGraphicsPath)]
    private static partial IntPtr CGColorSpaceCreateDeviceRGB();

    [LibraryImport(CoreGraphicsPath)]
    private static partial void CGColorSpaceRelease(IntPtr space);

    [LibraryImport(CoreGraphicsPath)]
    private static partial IntPtr CGDataProviderCreateWithCFData(IntPtr data);

    [LibraryImport(CoreGraphicsPath)]
    private static partial void CGDataProviderRelease(IntPtr provider);

    [LibraryImport(CoreGraphicsPath)]
    private static partial IntPtr CGImageCreate(
        nint width,
        nint height,
        nint bitsPerComponent,
        nint bitsPerPixel,
        nint bytesPerRow,
        IntPtr colorSpace,
        uint bitmapInfo,
        IntPtr provider,
        IntPtr decode,
        [MarshalAs(UnmanagedType.U1)] bool shouldInterpolate,
        int intent);

    [LibraryImport(CoreGraphicsPath)]
    internal static partial void CGImageRelease(IntPtr image);

    [LibraryImport(CoreGraphicsPath)]
    private static partial nint CGImageGetWidth(IntPtr image);

    [LibraryImport(CoreGraphicsPath)]
    private static partial nint CGImageGetHeight(IntPtr image);

    [LibraryImport(CoreGraphicsPath)]
    private static partial IntPtr CGBitmapContextCreate(
        IntPtr data,
        nint width,
        nint height,
        nint bitsPerComponent,
        nint bytesPerRow,
        IntPtr colorSpace,
        uint bitmapInfo);

    [LibraryImport(CoreGraphicsPath)]
    private static partial void CGContextDrawImage(
        IntPtr context,
        CoreGraphicsRect rect,
        IntPtr image);

    [LibraryImport(CoreGraphicsPath)]
    private static partial void CGContextRelease(IntPtr context);

    [LibraryImport(ImageIoPath)]
    private static partial IntPtr CGImageDestinationCreateWithData(
        IntPtr data,
        IntPtr type,
        nint count,
        IntPtr options);

    [LibraryImport(ImageIoPath)]
    private static partial void CGImageDestinationAddImage(
        IntPtr destination,
        IntPtr image,
        IntPtr properties);

    [LibraryImport(ImageIoPath)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool CGImageDestinationFinalize(IntPtr destination);

    /// <summary>
    /// Wraps BGRA32 pixels in a <c>CGImage</c>. The pixel data is copied, so the caller's buffer is
    /// free immediately; the returned image must be released with <see cref="CGImageRelease"/>.
    /// </summary>
    internal static unsafe IntPtr CreateImage(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        int stride)
    {
        IntPtr source = IntPtr.Zero;
        IntPtr provider = IntPtr.Zero;
        IntPtr colorSpace = IntPtr.Zero;
        try
        {
            fixed (byte* pointer = pixels)
            {
                source = CoreFoundationNative.CFDataCreate(
                    IntPtr.Zero,
                    (IntPtr)pointer,
                    pixels.Length);
            }

            if (source == IntPtr.Zero)
                throw new InvalidOperationException("The pixel buffer could not be copied.");

            provider = CGDataProviderCreateWithCFData(source);
            colorSpace = CGColorSpaceCreateDeviceRGB();
            if (provider == IntPtr.Zero || colorSpace == IntPtr.Zero)
                throw new InvalidOperationException("CoreGraphics rejected the pixel buffer.");

            var image = CGImageCreate(
                width,
                height,
                BitsPerComponent,
                BitsPerPixel,
                stride,
                colorSpace,
                Bgra32BitmapInfo,
                provider,
                IntPtr.Zero,
                false,
                DefaultRenderingIntent);
            return image != IntPtr.Zero
                ? image
                : throw new InvalidOperationException("CoreGraphics could not build the image.");
        }
        finally
        {
            if (colorSpace != IntPtr.Zero)
                CGColorSpaceRelease(colorSpace);
            if (provider != IntPtr.Zero)
                CGDataProviderRelease(provider);
            if (source != IntPtr.Zero)
                CoreFoundationNative.CFRelease(source);
        }
    }

    /// <summary>
    /// Reads a <c>CGImage</c> back as tightly packed BGRA32.
    /// </summary>
    /// <remarks>
    /// The image is drawn into a bitmap context whose format this method chooses, rather than being
    /// read through its own data provider. That is deliberate: a captured image can arrive in any
    /// colour space, alpha arrangement or row padding, and drawing normalises all of it in one step.
    /// A bitmap context lays its rows out top first, which is the order the contract's
    /// <c>ImageFrame</c> expects.
    /// </remarks>
    internal static unsafe byte[] ReadBgra32(IntPtr image, out int width, out int height, out int stride)
    {
        width = (int)CGImageGetWidth(image);
        height = (int)CGImageGetHeight(image);
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("The captured image has no pixels.");

        stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        IntPtr colorSpace = IntPtr.Zero;
        IntPtr context = IntPtr.Zero;
        try
        {
            colorSpace = CGColorSpaceCreateDeviceRGB();
            if (colorSpace == IntPtr.Zero)
                throw new InvalidOperationException("CoreGraphics rejected the colour space.");

            fixed (byte* buffer = pixels)
            {
                context = CGBitmapContextCreate(
                    (IntPtr)buffer,
                    width,
                    height,
                    BitsPerComponent,
                    stride,
                    colorSpace,
                    Bgra32BitmapInfo);
                if (context == IntPtr.Zero)
                    throw new InvalidOperationException("The bitmap context could not be created.");

                CGContextDrawImage(
                    context,
                    new CoreGraphicsRect { X = 0, Y = 0, Width = width, Height = height },
                    image);
            }

            return pixels;
        }
        finally
        {
            if (context != IntPtr.Zero)
                CGContextRelease(context);
            if (colorSpace != IntPtr.Zero)
                CGColorSpaceRelease(colorSpace);
        }
    }

    /// <summary>
    /// Encodes BGRA32 pixels as PNG, or throws with the stage that failed. The caller owns the
    /// returned bytes; no CoreFoundation handle escapes this method.
    /// </summary>
    internal static unsafe byte[] EncodePng(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        int stride)
    {
        NativeLibrary.Load(ImageIoPath);

        IntPtr source = IntPtr.Zero;
        IntPtr provider = IntPtr.Zero;
        IntPtr colorSpace = IntPtr.Zero;
        IntPtr image = IntPtr.Zero;
        IntPtr target = IntPtr.Zero;
        IntPtr destination = IntPtr.Zero;
        IntPtr type = IntPtr.Zero;
        try
        {
            fixed (byte* pointer = pixels)
            {
                source = CoreFoundationNative.CFDataCreate(
                    IntPtr.Zero,
                    (IntPtr)pointer,
                    pixels.Length);
            }

            if (source == IntPtr.Zero)
                throw new InvalidOperationException("The pixel buffer could not be copied.");

            provider = CGDataProviderCreateWithCFData(source);
            colorSpace = CGColorSpaceCreateDeviceRGB();
            if (provider == IntPtr.Zero || colorSpace == IntPtr.Zero)
                throw new InvalidOperationException("CoreGraphics rejected the pixel buffer.");

            image = CGImageCreate(
                width,
                height,
                BitsPerComponent,
                BitsPerPixel,
                stride,
                colorSpace,
                Bgra32BitmapInfo,
                provider,
                IntPtr.Zero,
                false,
                DefaultRenderingIntent);
            if (image == IntPtr.Zero)
                throw new InvalidOperationException("CoreGraphics could not build the image.");

            target = CoreFoundationNative.CFDataCreateMutable(IntPtr.Zero, 0);
            type = CoreFoundationNative.CreateString(PasteboardType.Png);
            destination = CGImageDestinationCreateWithData(target, type, 1, IntPtr.Zero);
            if (destination == IntPtr.Zero)
                throw new InvalidOperationException("The PNG encoder could not be created.");

            CGImageDestinationAddImage(destination, image, IntPtr.Zero);
            if (!CGImageDestinationFinalize(destination))
                throw new InvalidOperationException("The PNG encoder failed to write the image.");

            var length = CoreFoundationNative.CFDataGetLength(target);
            var bytes = CoreFoundationNative.CFDataGetBytePtr(target);
            if (length <= 0 || bytes == IntPtr.Zero)
                throw new InvalidOperationException("The PNG encoder produced no data.");

            var encoded = new byte[length];
            Marshal.Copy(bytes, encoded, 0, (int)length);
            return encoded;
        }
        finally
        {
            if (destination != IntPtr.Zero)
                CoreFoundationNative.CFRelease(destination);
            if (type != IntPtr.Zero)
                CoreFoundationNative.CFRelease(type);
            if (target != IntPtr.Zero)
                CoreFoundationNative.CFRelease(target);
            if (image != IntPtr.Zero)
                CGImageRelease(image);
            if (colorSpace != IntPtr.Zero)
                CGColorSpaceRelease(colorSpace);
            if (provider != IntPtr.Zero)
                CGDataProviderRelease(provider);
            if (source != IntPtr.Zero)
                CoreFoundationNative.CFRelease(source);
        }
    }
}
