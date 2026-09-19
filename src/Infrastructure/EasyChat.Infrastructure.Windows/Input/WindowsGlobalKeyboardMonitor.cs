using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.Windows.Input;

[SupportedOSPlatform("windows")]
public sealed class WindowsGlobalKeyboardMonitor : IGlobalKeyboardMonitor, IDisposable
{
    private readonly object _lifecycle = new();
    private readonly object _callbacksSync = new();
    private readonly Dictionary<long, Action<GlobalKeyboardEvent>> _callbacks = [];
    private readonly WindowsKeyboardHookCallback _procedure;
    private readonly IWindowsMessageThread _messageThread;
    private readonly IWindowsKeyboardHookBackend _backend;
    private readonly object _pollSync = new();
    private readonly object _publishSync = new();
    private readonly bool[] _pollStates = new bool[256];
    private readonly long[] _lastPublishedTicks = new long[256];
    private IntPtr _hook;
    private Timer? _pollTimer;
    private long _nextRegistrationId;
    private bool _disposed;

    public WindowsGlobalKeyboardMonitor()
        : this(new WindowsMessageThread(), new NativeWindowsKeyboardHookBackend())
    {
    }

    internal WindowsGlobalKeyboardMonitor(
        IWindowsMessageThread messageThread,
        IWindowsKeyboardHookBackend backend)
    {
        _messageThread = messageThread ?? throw new ArgumentNullException(nameof(messageThread));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _procedure = HandleHook;
    }

    public IKeyboardMonitorRegistration Start(Action<GlobalKeyboardEvent> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        long id;
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            id = ++_nextRegistrationId;
            lock (_callbacksSync)
                _callbacks.Add(id, callback);
        }

        try
        {
            _messageThread.Invoke(ReconcileHook);
            return new Registration(this, id);
        }
        catch
        {
            lock (_lifecycle)
            {
                lock (_callbacksSync)
                    _callbacks.Remove(id);
            }

            TryReconcileHook();
            throw;
        }
    }

    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed)
                return;

            _disposed = true;
            lock (_callbacksSync)
                _callbacks.Clear();
        }

        try
        {
            _messageThread.Invoke(ReconcileHook);
        }
        finally
        {
            _messageThread.Dispose();
        }
    }

    private void RemoveRegistration(long id)
    {
        lock (_lifecycle)
        {
            lock (_callbacksSync)
            {
                if (!_callbacks.Remove(id))
                    return;
            }
        }

        TryReconcileHook();
    }

    private void ReconcileHook()
    {
        bool shouldBeInstalled;
        lock (_lifecycle)
        {
            lock (_callbacksSync)
                shouldBeInstalled = !_disposed && _callbacks.Count > 0;
        }

        if (shouldBeInstalled && _hook == IntPtr.Zero)
        {
            InitializePollStates();
            _hook = _backend.Install(_procedure);
            if (_hook == IntPtr.Zero)
                throw new Win32Exception(_backend.LastError, "Unable to install the global keyboard hook.");
            _pollTimer = new Timer(PollKeyboardState, null, 8, 8);
        }
        else if (!shouldBeInstalled && _hook != IntPtr.Zero)
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
            if (!_backend.Uninstall(_hook))
                throw new Win32Exception(_backend.LastError, "Unable to remove the global keyboard hook.");
            _hook = IntPtr.Zero;
        }
    }

    private void TryReconcileHook()
    {
        try
        {
            _messageThread.Invoke(ReconcileHook);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private IntPtr HandleHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && IsKeyDownMessage(message))
        {
            var nativeEvent = _backend.ReadEvent(data);
            if ((nativeEvent.Flags & InjectedFlag) == 0)
                TryPublishKey(nativeEvent.VirtualKey);
        }

        return _backend.CallNext(_hook, code, message, data);
    }

    private void Publish(GlobalKeyboardEvent keyboardEvent)
    {
        Action<GlobalKeyboardEvent>[] callbacks;
        lock (_callbacksSync)
            callbacks = _callbacks.Values.ToArray();

        foreach (var callback in callbacks)
        {
            try
            {
                callback(keyboardEvent);
            }
            catch
            {
                // A recorder must not break the native keyboard hook callback.
            }
        }
    }

    private void InitializePollStates()
    {
        lock (_pollSync)
        {
            for (var virtualKey = 0; virtualKey < _pollStates.Length; virtualKey++)
                _pollStates[virtualKey] = IsPressed((ushort)virtualKey);
        }
    }

    private void PollKeyboardState(object? state)
    {
        if (Volatile.Read(ref _disposed))
            return;

        for (var virtualKey = 1; virtualKey < _pollStates.Length; virtualKey++)
        {
            var pressed = IsPressed((ushort)virtualKey);
            bool wasPressed;
            lock (_pollSync)
            {
                wasPressed = _pollStates[virtualKey];
                _pollStates[virtualKey] = pressed;
            }

            if (pressed && !wasPressed)
                TryPublishKey((ushort)virtualKey);
        }
    }

    private bool TryPublishKey(ushort virtualKey)
    {
        if (!TryGetKeyName(virtualKey, out var key))
            return false;

        var now = Environment.TickCount64;
        lock (_publishSync)
        {
            if (now - _lastPublishedTicks[virtualKey] < PublishDeduplicationWindowMs)
                return false;
            _lastPublishedTicks[virtualKey] = now;
        }

        Publish(new GlobalKeyboardEvent(key, ReadModifiers(virtualKey), true));
        return true;
    }

    private static bool IsKeyDownMessage(IntPtr message) =>
        message == KeyDownMessage || message == SystemKeyDownMessage;

    private static ShortcutModifiers ReadModifiers(ushort virtualKey)
    {
        var modifiers = ShortcutModifiers.None;
        if (IsPressed(LeftControl) || IsPressed(RightControl) || virtualKey is LeftControl or RightControl)
            modifiers |= ShortcutModifiers.Control;
        if (IsPressed(LeftAlt) || IsPressed(RightAlt) || virtualKey is LeftAlt or RightAlt)
            modifiers |= ShortcutModifiers.Alt;
        if (IsPressed(LeftShift) || IsPressed(RightShift) || virtualKey is LeftShift or RightShift)
            modifiers |= ShortcutModifiers.Shift;
        if (IsPressed(LeftMeta) || IsPressed(RightMeta) || virtualKey is LeftMeta or RightMeta)
            modifiers |= ShortcutModifiers.Meta;
        return modifiers;
    }

    private static bool IsPressed(ushort virtualKey) =>
        (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(ushort virtualKey);

    private static bool TryGetKeyName(ushort virtualKey, out string key)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            key = ((char)virtualKey).ToString();
            return true;
        }

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            key = $"D{virtualKey - 0x30}";
            return true;
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            key = $"F{virtualKey - 0x70 + 1}";
            return true;
        }

        if (virtualKey is >= 0x60 and <= 0x69)
        {
            key = $"NumPad{virtualKey - 0x60}";
            return true;
        }

        key = virtualKey switch
        {
            0x08 => "Back",
            0x09 => "Tab",
            0x0D => "Enter",
            0x10 => "Shift",
            0x11 => "Ctrl",
            0x12 => "Alt",
            0x13 => "Pause",
            0x14 => "CapsLock",
            0x90 => "NumLock",
            0x91 => "ScrollLock",
            0x1B => "Escape",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "PrintScreen",
            0x2D => "Insert",
            0x2E => "Delete",
            0x5B => "LWin",
            0x5C => "RWin",
            0x5D => "Apps",
            0xBA => "Oem1",
            0xBB => "OemPlus",
            0xBC => "OemComma",
            0xBD => "OemMinus",
            0xBE => "OemPeriod",
            0xBF => "Oem2",
            0xC0 => "Oem3",
            0xDB => "Oem4",
            0xDC => "Oem5",
            0xDD => "Oem6",
            0xDE => "Oem7",
            _ => string.Empty
        };
        return key.Length > 0;
    }

    private const int KeyDownMessage = 0x0100;
    private const int SystemKeyDownMessage = 0x0104;
    private const uint InjectedFlag = 0x00000010;
    private const long PublishDeduplicationWindowMs = 30;
    private const ushort LeftControl = 0xA2;
    private const ushort RightControl = 0xA3;
    private const ushort LeftAlt = 0xA4;
    private const ushort RightAlt = 0xA5;
    private const ushort LeftShift = 0xA0;
    private const ushort RightShift = 0xA1;
    private const ushort LeftMeta = 0x5B;
    private const ushort RightMeta = 0x5C;

    private sealed class Registration(WindowsGlobalKeyboardMonitor owner, long id)
        : IKeyboardMonitorRegistration
    {
        private WindowsGlobalKeyboardMonitor? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.RemoveRegistration(id);
    }
}

internal delegate IntPtr WindowsKeyboardHookCallback(int code, IntPtr message, IntPtr data);

internal interface IWindowsKeyboardHookBackend
{
    int LastError { get; }
    IntPtr Install(WindowsKeyboardHookCallback callback);
    bool Uninstall(IntPtr hook);
    NativeKeyboardHookEvent ReadEvent(IntPtr data);
    IntPtr CallNext(IntPtr hook, int code, IntPtr message, IntPtr data);
}

internal readonly record struct NativeKeyboardHookEvent(
    ushort VirtualKey,
    uint ScanCode,
    uint Flags,
    uint Time,
    UIntPtr ExtraInfo);

[SupportedOSPlatform("windows")]
internal sealed class NativeWindowsKeyboardHookBackend : IWindowsKeyboardHookBackend
{
    public int LastError => Marshal.GetLastWin32Error();

    public IntPtr Install(WindowsKeyboardHookCallback callback)
    {
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = GetModuleHandle(module?.ModuleName);
        return SetWindowsHookEx(
            13,
            callback,
            moduleHandle,
            0);
    }

    public bool Uninstall(IntPtr hook) => UnhookWindowsHookEx(hook);

    public NativeKeyboardHookEvent ReadEvent(IntPtr data)
    {
        var value = Marshal.PtrToStructure<NativeKeyboardHookStruct>(data);
        return new NativeKeyboardHookEvent(
            checked((ushort)value.VirtualKey),
            value.ScanCode,
            value.Flags,
            value.Time,
            value.ExtraInfo);
    }

    public IntPtr CallNext(IntPtr hook, int code, IntPtr message, IntPtr data) =>
        CallNextHookEx(hook, code, message, data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookType,
        WindowsKeyboardHookCallback callback,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeKeyboardHookStruct
    {
        public readonly uint VirtualKey;
        public readonly uint ScanCode;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly UIntPtr ExtraInfo;
    }
}
