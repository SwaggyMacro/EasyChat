using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>
/// Reads the bytes out of a <c>CMSampleBuffer</c>.
/// </summary>
/// <remarks>
/// ScreenCaptureKit delivers audio as non-interleaved 32-bit float. Requesting a single channel is
/// what makes this simple: with one plane the sample buffer's data block is already a contiguous
/// run of floats, so it can be copied straight out. Asking for stereo would hand back two separate
/// planes that would have to be interleaved first.
/// </remarks>
internal static partial class CoreMediaNative
{
    private const string LibraryPath =
        "/System/Library/Frameworks/CoreMedia.framework/CoreMedia";

    [LibraryImport(LibraryPath)]
    private static partial IntPtr CMSampleBufferGetDataBuffer(IntPtr sampleBuffer);

    [LibraryImport(LibraryPath)]
    private static partial nint CMBlockBufferGetDataLength(IntPtr blockBuffer);

    [LibraryImport(LibraryPath)]
    private static partial int CMBlockBufferCopyDataBytes(
        IntPtr blockBuffer,
        nint offset,
        nint length,
        IntPtr destination);

    /// <summary>Copies the sample buffer's audio as 32-bit floats, or an empty span when it has none.</summary>
    internal static unsafe float[] ReadFloat32(IntPtr sampleBuffer)
    {
        if (sampleBuffer == IntPtr.Zero)
            return [];

        var block = CMSampleBufferGetDataBuffer(sampleBuffer);
        if (block == IntPtr.Zero)
            return [];

        var length = CMBlockBufferGetDataLength(block);
        if (length < sizeof(float))
            return [];

        var samples = new float[length / sizeof(float)];
        fixed (float* destination = samples)
        {
            if (CMBlockBufferCopyDataBytes(block, 0, samples.Length * sizeof(float), (IntPtr)destination) != 0)
                return [];
        }

        return samples;
    }
}
