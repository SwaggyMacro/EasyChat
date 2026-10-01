using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>
/// Plays an audio file through <c>AVPlayer</c>.
/// </summary>
/// <remarks>
/// <c>AVPlayer</c> is chosen over <c>AVAudioPlayer</c> for one reason: it is the only simple player
/// on macOS that exposes <c>audioOutputDeviceUniqueID</c>, and routing speech to a chosen device is
/// exactly what interpretation needs. Playback itself requires no privacy approval.
/// </remarks>
internal static class AVPlayerNative
{
    private const string LibraryPath =
        "/System/Library/Frameworks/AVFoundation.framework/AVFoundation";

    /// <summary>Value of <c>AVPlayerItemStatusFailed</c>.</summary>
    private const nint ItemFailed = 2;

    /// <summary>Value of <c>AVPlayerItemStatusReadyToPlay</c>.</summary>
    internal const nint ItemReady = 1;

    private static readonly Lazy<bool> Loaded = new(
        () => NativeLibrary.Load(LibraryPath) != IntPtr.Zero,
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Creates a retained player for a local file. The caller owns it and must call
    /// <see cref="Release"/>.
    /// </summary>
    internal static IntPtr Create(string filePath, string? outputDeviceUid)
    {
        EnsureLoaded();
        var url = ObjectiveCNative.SendReturningHandle(
            ObjectiveCNative.GetClass("NSURL"),
            ObjectiveCNative.GetSelector("fileURLWithPath:"),
            FoundationNative.CreateString(filePath));
        if (url == IntPtr.Zero)
            return IntPtr.Zero;

        var player = ObjectiveCNative.SendReturningHandle(
            ObjectiveCNative.GetClass("AVPlayer"),
            ObjectiveCNative.GetSelector("playerWithURL:"),
            url);
        if (player == IntPtr.Zero)
            return IntPtr.Zero;

        if (!string.IsNullOrWhiteSpace(outputDeviceUid))
        {
            ObjectiveCNative.Send(
                player,
                ObjectiveCNative.GetSelector("setAudioOutputDeviceUniqueID:"),
                FoundationNative.CreateString(outputDeviceUid));
        }

        return ObjectiveCNative.SendReturningHandle(
            player,
            ObjectiveCNative.GetSelector("retain"));
    }

    internal static void Play(IntPtr player) =>
        ObjectiveCNative.Send(player, ObjectiveCNative.GetSelector("play"));

    internal static void Pause(IntPtr player) =>
        ObjectiveCNative.Send(player, ObjectiveCNative.GetSelector("pause"));

    internal static void Release(IntPtr player)
    {
        if (player != IntPtr.Zero)
            ObjectiveCNative.Send(player, ObjectiveCNative.GetSelector("release"));
    }

    /// <summary>Status of the item being played: ready, failed, or still unknown.</summary>
    internal static nint ItemStatus(IntPtr player)
    {
        var item = ObjectiveCNative.SendReturningHandle(
            player,
            ObjectiveCNative.GetSelector("currentItem"));
        return item == IntPtr.Zero
            ? ItemFailed
            : ObjectiveCNative.SendReturningNInt(item, ObjectiveCNative.GetSelector("status"));
    }

    internal static bool HasFailed(IntPtr player) => ItemStatus(player) == ItemFailed;

    /// <summary>
    /// The playback rate: zero before playback starts, one while playing, and back to zero once the
    /// item reaches its end.
    /// </summary>
    /// <remarks>
    /// Polling this is used rather than key-value observation, because observing would mean
    /// building an Objective-C class at runtime to receive the callback, for clips that last
    /// seconds. The caller distinguishes "not started yet" from "finished" by requiring that it has
    /// seen the rate rise first.
    /// </remarks>
    internal static float Rate(IntPtr player) =>
        ObjectiveCNative.SendReturningSingle(player, ObjectiveCNative.GetSelector("rate"));

    private static void EnsureLoaded()
    {
        if (!Loaded.Value)
            throw new PlatformNotSupportedException("AVFoundation could not be loaded.");
    }
}
