using System;

namespace EasyChat.Services.Abstractions;

public interface IHotKeyManager : IDisposable
{
    // Register a hotkey. Action callback is invoked when hotkey is pressed.
    // Returns an IDisposable that unregisters the hotkey when disposed.
    IDisposable? Register(KeyModifiers modifiers, Key key, Action callback);

    /// <summary>
    ///     Try to register a hotkey to test if it's available.
    ///     Returns true if the hotkey can be registered (no conflict with other software).
    /// </summary>
    bool TryRegister(KeyModifiers modifiers, Key key);
}

// Custom enum to avoid dependency on specific UI framework or Win32 types in the interface
[Flags]
public enum KeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
    NoRepeat = 0x4000
}

public enum Key
{
    // Define only keys we might need, or map generic ones. 
    // For now, mapping a few standard ones.
    KeyZ, // Renaming or keeping Z for compatibility? The user used "Z". The file has A..Z without prefix.
    // Actually I should just append.
    A = 65,
    B,
    C,
    D,
    E,
    F,
    G,
    H,
    I,
    J,
    K,
    L,
    M,
    N,
    O,
    P,
    Q,
    R,
    S,
    T,
    U,
    V,
    W,
    X,
    Y,
    Z,
    // Digits
    D0,
    D1,
    D2,
    D3,
    D4,
    D5,
    D6,
    D7,
    D8,
    D9,
    // NumPad
    NumPad0,
    NumPad1,
    NumPad2,
    NumPad3,
    NumPad4,
    NumPad5,
    NumPad6,
    NumPad7,
    NumPad8,
    NumPad9,
    // Function Keys
    F1 = 112,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
    F13,
    F14,
    F15,
    F16,
    F17,
    F18,
    F19,
    F20,
    F21,
    F22,
    F23,
    F24,
    // Common
    Escape,
    Tab,
    Space,
    Back,
    Enter,
    Return = Enter, // Alias
    Insert,
    Delete,
    PageUp,
    PageDown,
    Home,
    End,
    Left,
    Up,
    Right,
    Down,
    // Modifiers/System (if needed for Key mapping though Modifiers are separate)
    LWin,
    RWin,
    Apps,
    // Media/OEM
    OemSemicolon,
    OemPlus,
    OemComma,
    OemMinus,
    OemPeriod,
    OemQuestion,
    OemTilde, 
    OemOpenBrackets,
    OemPipe,
    OemCloseBrackets,
    OemQuotes
}