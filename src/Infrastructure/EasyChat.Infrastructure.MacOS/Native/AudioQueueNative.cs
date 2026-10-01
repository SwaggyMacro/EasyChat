using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>Layout of <c>AudioStreamBasicDescription</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AudioStreamBasicDescription
{
    internal double SampleRate;
    internal uint FormatId;
    internal uint FormatFlags;
    internal uint BytesPerPacket;
    internal uint FramesPerPacket;
    internal uint BytesPerFrame;
    internal uint ChannelsPerFrame;
    internal uint BitsPerChannel;
    internal uint Reserved;
}

/// <summary>
/// Recording through <c>AudioQueue</c>.
/// </summary>
/// <remarks>
/// AudioQueue is chosen over <c>AVCaptureSession</c> because it is a plain C API: its callback is an
/// ordinary function pointer rather than a delegate object that would have to be built at runtime.
/// It also converts from the device's own rate and channel count to whatever format is asked for, so
/// a 48 kHz stereo microphone and a 16 kHz mono recogniser need no conversion code here at all —
/// including when the device changes rate mid-session, as AirPods do when they switch profile.
/// </remarks>
internal static partial class AudioQueueNative
{
    private const string LibraryPath =
        "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    /// <summary>Four-character code <c>lpcm</c>, the value of <c>kAudioFormatLinearPCM</c>.</summary>
    private const uint LinearPcmFormat = 0x6C70636D;

    /// <summary>
    /// <c>kAudioFormatFlagIsSignedInteger | kAudioFormatFlagIsPacked</c>: the signed, gapless
    /// little-endian samples the contract's PCM format describes.
    /// </summary>
    private const uint SignedPackedFlags = 4 | 8;

    /// <summary>Four-character code <c>cdev</c>, the value of <c>kAudioQueueProperty_CurrentDevice</c>.</summary>
    private const uint CurrentDeviceProperty = 0x63646576;

    /// <summary>Offsets into <c>AudioQueueBuffer</c>, whose first member is followed by padding.</summary>
    private const int BufferDataPointerOffset = 8;

    private const int BufferUsedBytesOffset = 16;

    [LibraryImport(LibraryPath)]
    internal static partial int AudioQueueNewInput(
        ref AudioStreamBasicDescription format,
        IntPtr callback,
        IntPtr userData,
        IntPtr callbackRunLoop,
        IntPtr callbackRunLoopMode,
        uint flags,
        out IntPtr queue);

    [LibraryImport(LibraryPath)]
    internal static partial int AudioQueueAllocateBuffer(
        IntPtr queue,
        uint byteSize,
        out IntPtr buffer);

    [LibraryImport(LibraryPath)]
    internal static partial int AudioQueueEnqueueBuffer(
        IntPtr queue,
        IntPtr buffer,
        uint packetCount,
        IntPtr packetDescriptions);

    [LibraryImport(LibraryPath)]
    internal static partial int AudioQueueStart(IntPtr queue, IntPtr startTime);

    [LibraryImport(LibraryPath)]
    internal static partial int AudioQueueStop(
        IntPtr queue,
        [MarshalAs(UnmanagedType.U1)] bool immediate);

    [LibraryImport(LibraryPath)]
    internal static partial int AudioQueueDispose(
        IntPtr queue,
        [MarshalAs(UnmanagedType.U1)] bool immediate);

    [LibraryImport(LibraryPath)]
    internal static partial int AudioQueueSetProperty(
        IntPtr queue,
        uint propertyId,
        ref IntPtr value,
        uint valueSize);

    /// <summary>
    /// Describes the format to record in. AudioQueue converts from whatever the device produces, so
    /// this is the format the callback receives regardless of the hardware.
    /// </summary>
    internal static AudioStreamBasicDescription DescribePcm(
        int sampleRateHz,
        int channelCount,
        int bitsPerSample)
    {
        var bytesPerFrame = (uint)(channelCount * (bitsPerSample / 8));
        return new AudioStreamBasicDescription
        {
            SampleRate = sampleRateHz,
            FormatId = LinearPcmFormat,
            FormatFlags = SignedPackedFlags,
            BytesPerPacket = bytesPerFrame,
            FramesPerPacket = 1,
            BytesPerFrame = bytesPerFrame,
            ChannelsPerFrame = (uint)channelCount,
            BitsPerChannel = (uint)bitsPerSample,
            Reserved = 0
        };
    }

    /// <summary>Points the queue at one particular input device rather than the system default.</summary>
    internal static int SetInputDevice(IntPtr queue, string deviceUid)
    {
        var uid = CoreFoundationNative.CreateString(deviceUid);
        try
        {
            return AudioQueueSetProperty(
                queue,
                CurrentDeviceProperty,
                ref uid,
                (uint)IntPtr.Size);
        }
        finally
        {
            CoreFoundationNative.CFRelease(uid);
        }
    }

    /// <summary>Copies the audio a filled buffer holds.</summary>
    internal static byte[] ReadBuffer(IntPtr buffer)
    {
        var used = (int)(uint)Marshal.ReadInt32(buffer, BufferUsedBytesOffset);
        if (used <= 0)
            return [];

        var data = Marshal.ReadIntPtr(buffer, BufferDataPointerOffset);
        if (data == IntPtr.Zero)
            return [];

        var bytes = new byte[used];
        Marshal.Copy(data, bytes, 0, used);
        return bytes;
    }
}
