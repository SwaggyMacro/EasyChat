using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using EasyChat.Services.Abstractions;
using GlobalHotKeys;
using GlobalHotKeys.Native.Types;
using Microsoft.Extensions.Logging;

namespace EasyChat.Services.HotKey;

public class WindowsHotKeyManager : IHotKeyManager
{
    private readonly CompositeDisposable _disposables = new();
    private readonly HotKeyManager _hotKeyManager;
    private readonly ILogger<WindowsHotKeyManager> _logger;

    public WindowsHotKeyManager(ILogger<WindowsHotKeyManager> logger)
    {
        _logger = logger;
        _hotKeyManager = new HotKeyManager();
        _logger.LogDebug("WindowsHotKeyManager initialized");
    }

    public IDisposable Register(KeyModifiers modifiers, Key key, Action callback)
    {
        var nativeModifiers = MapModifiers(modifiers);
        var nativeKey = MapKey(key);

        try
        {
            var registration = _hotKeyManager.Register(nativeKey, nativeModifiers);

            var subscription = _hotKeyManager.HotKeyPressed
                .Where(hk => hk.Id == registration.Id)
                .Subscribe(_ =>
                {
                    _logger.LogDebug("Hotkey triggered: {Modifiers}+{Key}", modifiers, key);
                    callback();
                });

            var disposable = Disposable.Create(() =>
            {
                subscription.Dispose();
                registration.Dispose();
                _logger.LogDebug("Hotkey unregistered: {Modifiers}+{Key}", modifiers, key);
            });

            _disposables.Add(disposable);
            _logger.LogInformation("Hotkey registered: {Modifiers}+{Key}", modifiers, key);
            return disposable;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register hotkey: {Modifiers}+{Key}", modifiers, key);
            return Disposable.Empty;
        }
    }

    public bool TryRegister(KeyModifiers modifiers, Key key)
    {
        var nativeModifiers = MapModifiers(modifiers);
        var nativeKey = MapKey(key);

        try
        {
            var registration = _hotKeyManager.Register(nativeKey, nativeModifiers);
            registration.Dispose();
            _logger.LogDebug("Hotkey availability check passed: {Modifiers}+{Key}", modifiers, key);
            return true;
        }
        catch
        {
            _logger.LogDebug("Hotkey availability check failed: {Modifiers}+{Key}", modifiers, key);
            return false;
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
        _hotKeyManager.Dispose();
        _logger.LogDebug("WindowsHotKeyManager disposed");
    }

    private Modifiers MapModifiers(KeyModifiers modifiers)
    {
        Modifiers result = 0;
        if (modifiers.HasFlag(KeyModifiers.Alt)) result |= Modifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Control)) result |= Modifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Shift)) result |= Modifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Windows)) result |= Modifiers.Win;
        if (modifiers.HasFlag(KeyModifiers.NoRepeat)) result |= Modifiers.NoRepeat;

        return result;
    }

    private VirtualKeyCode MapKey(Key key)
    {
        return key switch
        {
            // Standard Keys
            Key.Escape => VirtualKeyCode.VK_ESCAPE,
            Key.Back => VirtualKeyCode.VK_BACK,
            Key.Tab => VirtualKeyCode.VK_TAB,
            Key.Enter => VirtualKeyCode.VK_RETURN,
            Key.Space => VirtualKeyCode.VK_SPACE,
            Key.PageUp => VirtualKeyCode.VK_PRIOR,
            Key.PageDown => VirtualKeyCode.VK_NEXT,
            Key.End => VirtualKeyCode.VK_END,
            Key.Home => VirtualKeyCode.VK_HOME,
            Key.Left => VirtualKeyCode.VK_LEFT,
            Key.Up => VirtualKeyCode.VK_UP,
            Key.Right => VirtualKeyCode.VK_RIGHT,
            Key.Down => VirtualKeyCode.VK_DOWN,
            Key.Insert => VirtualKeyCode.VK_INSERT,
            Key.Delete => VirtualKeyCode.VK_DELETE,
            
            // Digits
            Key.D0 => VirtualKeyCode.KEY_0,
            Key.D1 => VirtualKeyCode.KEY_1,
            Key.D2 => VirtualKeyCode.KEY_2,
            Key.D3 => VirtualKeyCode.KEY_3,
            Key.D4 => VirtualKeyCode.KEY_4,
            Key.D5 => VirtualKeyCode.KEY_5,
            Key.D6 => VirtualKeyCode.KEY_6,
            Key.D7 => VirtualKeyCode.KEY_7,
            Key.D8 => VirtualKeyCode.KEY_8,
            Key.D9 => VirtualKeyCode.KEY_9,

            // Letters
            Key.A => VirtualKeyCode.KEY_A,
            Key.B => VirtualKeyCode.KEY_B,
            Key.C => VirtualKeyCode.KEY_C,
            Key.D => VirtualKeyCode.KEY_D,
            Key.E => VirtualKeyCode.KEY_E,
            Key.F => VirtualKeyCode.KEY_F,
            Key.G => VirtualKeyCode.KEY_G,
            Key.H => VirtualKeyCode.KEY_H,
            Key.I => VirtualKeyCode.KEY_I,
            Key.J => VirtualKeyCode.KEY_J,
            Key.K => VirtualKeyCode.KEY_K,
            Key.L => VirtualKeyCode.KEY_L,
            Key.M => VirtualKeyCode.KEY_M,
            Key.N => VirtualKeyCode.KEY_N,
            Key.O => VirtualKeyCode.KEY_O,
            Key.P => VirtualKeyCode.KEY_P,
            Key.Q => VirtualKeyCode.KEY_Q,
            Key.R => VirtualKeyCode.KEY_R,
            Key.S => VirtualKeyCode.KEY_S,
            Key.T => VirtualKeyCode.KEY_T,
            Key.U => VirtualKeyCode.KEY_U,
            Key.V => VirtualKeyCode.KEY_V,
            Key.W => VirtualKeyCode.KEY_W,
            Key.X => VirtualKeyCode.KEY_X,
            Key.Y => VirtualKeyCode.KEY_Y,
            Key.Z => VirtualKeyCode.KEY_Z,

            // Special
            Key.LWin => VirtualKeyCode.VK_LWIN,
            Key.RWin => VirtualKeyCode.VK_RWIN,
            Key.Apps => VirtualKeyCode.VK_APPS,

            // NumPad
            Key.NumPad0 => VirtualKeyCode.VK_NUMPAD0,
            Key.NumPad1 => VirtualKeyCode.VK_NUMPAD1,
            Key.NumPad2 => VirtualKeyCode.VK_NUMPAD2,
            Key.NumPad3 => VirtualKeyCode.VK_NUMPAD3,
            Key.NumPad4 => VirtualKeyCode.VK_NUMPAD4,
            Key.NumPad5 => VirtualKeyCode.VK_NUMPAD5,
            Key.NumPad6 => VirtualKeyCode.VK_NUMPAD6,
            Key.NumPad7 => VirtualKeyCode.VK_NUMPAD7,
            Key.NumPad8 => VirtualKeyCode.VK_NUMPAD8,
            Key.NumPad9 => VirtualKeyCode.VK_NUMPAD9,

            // Function Keys
            Key.F1 => VirtualKeyCode.VK_F1,
            Key.F2 => VirtualKeyCode.VK_F2,
            Key.F3 => VirtualKeyCode.VK_F3,
            Key.F4 => VirtualKeyCode.VK_F4,
            Key.F5 => VirtualKeyCode.VK_F5,
            Key.F6 => VirtualKeyCode.VK_F6,
            Key.F7 => VirtualKeyCode.VK_F7,
            Key.F8 => VirtualKeyCode.VK_F8,
            Key.F9 => VirtualKeyCode.VK_F9,
            Key.F10 => VirtualKeyCode.VK_F10,
            Key.F11 => VirtualKeyCode.VK_F11,
            Key.F12 => VirtualKeyCode.VK_F12,
            Key.F13 => VirtualKeyCode.VK_F13,
            Key.F14 => VirtualKeyCode.VK_F14,
            Key.F15 => VirtualKeyCode.VK_F15,
            Key.F16 => VirtualKeyCode.VK_F16,
            Key.F17 => VirtualKeyCode.VK_F17,
            Key.F18 => VirtualKeyCode.VK_F18,
            Key.F19 => VirtualKeyCode.VK_F19,
            Key.F20 => VirtualKeyCode.VK_F20,
            Key.F21 => VirtualKeyCode.VK_F21,
            Key.F22 => VirtualKeyCode.VK_F22,
            Key.F23 => VirtualKeyCode.VK_F23,
            Key.F24 => VirtualKeyCode.VK_F24,

            // OEM
            Key.OemSemicolon => VirtualKeyCode.VK_OEM_1,
            Key.OemPlus => VirtualKeyCode.VK_OEM_PLUS,
            Key.OemComma => VirtualKeyCode.VK_OEM_COMMA,
            Key.OemMinus => VirtualKeyCode.VK_OEM_MINUS,
            Key.OemPeriod => VirtualKeyCode.VK_OEM_PERIOD,
            Key.OemQuestion => VirtualKeyCode.VK_OEM_2,
            Key.OemTilde => VirtualKeyCode.VK_OEM_3,
            Key.OemOpenBrackets => VirtualKeyCode.VK_OEM_4,
            Key.OemPipe => VirtualKeyCode.VK_OEM_5,
            Key.OemCloseBrackets => VirtualKeyCode.VK_OEM_6,
            Key.OemQuotes => VirtualKeyCode.VK_OEM_7,

            _ => (VirtualKeyCode)(int)key
        };
    }
}