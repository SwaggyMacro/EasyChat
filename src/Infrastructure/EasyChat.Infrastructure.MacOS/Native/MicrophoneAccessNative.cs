using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

internal enum MediaAuthorizationStatus : long
{
    NotDetermined = 0,
    Restricted = 1,
    Denied = 2,
    Authorized = 3
}

/// <summary>
/// Microphone (TCC) access through <c>AVCaptureDevice</c>. AVFoundation exposes no C entry point for
/// this, so the check is an Objective-C class message and the request carries a global completion
/// block. The block is allocated once and intentionally never freed: libclosure must be able to read
/// its flags after the invoke returns.
/// </summary>
internal static class MicrophoneAccessNative
{
    private const string AvFoundationLibraryPath =
        "/System/Library/Frameworks/AVFoundation.framework/AVFoundation";

    private static readonly Lazy<IntPtr> AudioMediaType = new(
        () => Marshal.ReadIntPtr(NativeLibrary.GetExport(
            NativeLibrary.Load(AvFoundationLibraryPath),
            "AVMediaTypeAudio")),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<IntPtr> CompletionBlock = new(
        CreateCompletionBlock,
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lock Gate = new();

    private static TaskCompletionSource<bool>? _pending;

    internal static MediaAuthorizationStatus CheckStatus() =>
        (MediaAuthorizationStatus)ObjectiveCNative.SendReturningNInt(
            ObjectiveCNative.GetClass("AVCaptureDevice"),
            ObjectiveCNative.GetSelector("authorizationStatusForMediaType:"),
            AudioMediaType.Value);

    /// <summary>
    /// Triggers the system microphone prompt. Concurrent callers share the single in-flight request
    /// because macOS itself only ever shows one prompt.
    /// </summary>
    internal static Task<bool> RequestAccessAsync()
    {
        TaskCompletionSource<bool> completion;
        lock (Gate)
        {
            if (_pending is { } inFlight)
                return inFlight.Task;

            completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = completion;
        }

        try
        {
            ObjectiveCNative.Send(
                ObjectiveCNative.GetClass("AVCaptureDevice"),
                ObjectiveCNative.GetSelector("requestAccessForMediaType:completionHandler:"),
                AudioMediaType.Value,
                CompletionBlock.Value);
        }
        catch (Exception exception)
        {
            Complete(completion, exception);
        }

        return completion.Task;
    }

    private static unsafe IntPtr CreateCompletionBlock() =>
        ObjectiveCBlockFactory.CreateGlobalBlock(
            (IntPtr)(delegate* unmanaged<IntPtr, byte, void>)&OnAccessDecided);

    [UnmanagedCallersOnly]
    private static void OnAccessDecided(IntPtr block, byte granted)
    {
        TaskCompletionSource<bool>? completion;
        lock (Gate)
        {
            completion = _pending;
            _pending = null;
        }

        completion?.TrySetResult(granted != 0);
    }

    private static void Complete(TaskCompletionSource<bool> completion, Exception exception)
    {
        lock (Gate)
        {
            if (ReferenceEquals(_pending, completion))
                _pending = null;
        }

        completion.TrySetException(exception);
    }
}
