using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace EasyChat.Infrastructure.Windows.Input;

[SupportedOSPlatform("windows")]
internal sealed class WindowsNativeInputBackend
{
    public IntPtr GetForegroundWindow() => GetForegroundWindowNative();

    public void ActivateWindow(IntPtr window, ILogger logger)
    {
        var targetThreadId = GetWindowThreadProcessId(window, out _);
        var currentThreadId = GetCurrentThreadId();
        var attached = false;

        try
        {
            if (currentThreadId != targetThreadId)
            {
                attached = AttachThreadInput(currentThreadId, targetThreadId, true);
                if (!attached)
                    logger.LogWarning($"AttachThreadInput failed using ids: {currentThreadId} -> {targetThreadId}");
            }

            var activated = SetForegroundWindowNative(window);
            if (!activated)
            {
                BringWindowToTop(window);
                activated = SetForegroundWindowNative(window);
            }

            if (!activated)
            {
                SwitchToThisWindow(window, true);
                activated = SetForegroundWindowNative(window);
            }

            if (!activated)
            {
                keybd_event(VirtualKeyMenu, 0, 0, 0);
                keybd_event(VirtualKeyMenu, 0, KeyEventKeyUp, 0);
                activated = SetForegroundWindowNative(window);
            }

            if (!activated)
                logger.LogWarning($"All attempts to SetForegroundWindow failed for hWnd: {window}");

            SetFocusNative(window);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in SetForegroundWindow");
        }
        finally
        {
            if (attached)
                AttachThreadInput(currentThreadId, targetThreadId, false);
        }
    }

    public void SetFocus(IntPtr window) => SetFocusNative(window);

    public void PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam) =>
        PostMessageNative(window, message, wParam, lParam);

    public void SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam) =>
        SendMessageNative(window, message, wParam, lParam);

    public uint SendInputs(IReadOnlyList<WindowsKeyboardInputEvent> keyboardInputs)
    {
        ArgumentNullException.ThrowIfNull(keyboardInputs);

        var inputs = new NativeInput[keyboardInputs.Count];
        for (var index = 0; index < keyboardInputs.Count; index++)
        {
            var input = keyboardInputs[index];
            inputs[index] = new NativeInput
            {
                Type = InputKeyboard,
                Value = new NativeInputUnion
                {
                    Keyboard = new NativeKeyboardInput
                    {
                        VirtualKey = input.VirtualKey,
                        ScanCode = input.ScanCode,
                        Flags = input.Flags
                    }
                }
            };
        }

        return SendInputNative(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeInput>());
    }

    private const uint InputKeyboard = 1;
    private const byte VirtualKeyMenu = 0x12;
    private const uint KeyEventKeyUp = 0x0002;

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern IntPtr GetForegroundWindowNative();

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindowNative(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "SetFocus", SetLastError = true)]
    private static extern IntPtr SetFocusNative(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "PostMessage", CharSet = CharSet.Auto)]
    private static extern IntPtr PostMessageNative(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageNative(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(
        uint attachingThread,
        uint targetThread,
        [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("user32.dll", EntryPoint = "SendInput", SetLastError = true)]
    private static extern uint SendInputNative(
        uint inputCount,
        NativeInput[] inputs,
        int inputSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void SwitchToThisWindow(
        IntPtr window,
        [MarshalAs(UnmanagedType.Bool)] bool altTab);

    [DllImport("user32.dll")]
    private static extern void keybd_event(
        byte virtualKey,
        byte scanCode,
        uint flags,
        int extraInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeInputUnion Value;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)] public NativeMouseInput Mouse;
        [FieldOffset(0)] public NativeKeyboardInput Keyboard;
        [FieldOffset(0)] public NativeHardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeHardwareInput
    {
        public uint Message;
        public ushort ParamLow;
        public ushort ParamHigh;
    }
}
internal readonly record struct WindowsKeyboardInputEvent(
    ushort VirtualKey,
    ushort ScanCode,
    uint Flags)
{
    public const uint KeyUp = 0x0002;
    public const uint Unicode = 0x0004;
}
[SupportedOSPlatform("windows")]
internal sealed class WindowsWindowStyleBackend
{
    private const int ExtendedStyle = -20;
    private const int NoActivate = 0x08000000;
    private const int Transparent = 0x00000020;
    private const int Layered = 0x00080000;
    private const uint DisplayAffinityNone = 0x00000000;
    private const uint ExcludeFromCapture = 0x00000011;

    public void ConfigureNoActivate(IntPtr window, ILogger logger)
    {
        if (window == IntPtr.Zero)
        {
            logger.LogWarning("SetWindowNoActivate called with null handle");
            return;
        }

        try
        {
            var style = GetWindowLong(window, ExtendedStyle);
            SetWindowLong(window, ExtendedStyle, style | NoActivate);
            logger.LogDebug("Applied WS_EX_NOACTIVATE to window handle: {Handle}", window);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set WS_EX_NOACTIVATE on window");
        }
    }

    public void SetClickThrough(IntPtr window, bool enabled)
    {
        var style = GetWindowLong(window, ExtendedStyle);
        if (enabled)
        {
            if ((style & Transparent) == 0)
                SetWindowLong(window, ExtendedStyle, style | Transparent | Layered);
        }
        else if ((style & Transparent) != 0)
        {
            SetWindowLong(window, ExtendedStyle, style & ~Transparent);
        }
    }

    public void BringToFrontWithoutActivating(IntPtr window, ILogger logger)
    {
        if (window == IntPtr.Zero)
        {
            logger.LogWarning("BringToFrontWithoutActivating called with null handle");
            return;
        }

        try
        {
            SetWindowPos(
                window,
                Topmost,
                0,
                0,
                0,
                0,
                NoMove | NoSize | NoActivatePosition);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to bring window to the top without activating it");
        }
    }

    public bool TrySetExcludedFromCapture(IntPtr window, bool enabled)
    {
        if (window == IntPtr.Zero)
            return false;

        try
        {
            return SetWindowDisplayAffinity(
                window,
                enabled ? ExcludeFromCapture : DisplayAffinityNone);
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr window, int index, int newStyle);

    private const uint NoMove = 0x0002;
    private const uint NoSize = 0x0001;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivatePosition = 0x0010;
    private static readonly IntPtr Topmost = new(-1);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint displayAffinity);
}
