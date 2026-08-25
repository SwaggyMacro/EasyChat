# EasyChat IME composition observer

This is a diagnostic-only x64 `WH_CALLWNDPROC` hook. It observes
`WM_IME_STARTCOMPOSITION`, `WM_IME_COMPOSITION`, and `WM_IME_ENDCOMPOSITION` in
GUI processes and never changes the message or input text.

The native DLL writes UTF-8 lines to:

```text
%TEMP%\EasyChat.ImeCompositionHook.log
```

It also sends the same lines to `OutputDebugStringW`. Password-style edit
controls are logged with `<redacted>` instead of composition text.

Build from a Visual Studio Developer Command Prompt:

```text
msbuild EasyChat.ImeHook.Native.vcxproj /p:Configuration=Debug /p:Platform=x64
```

The DLL must be copied next to `EasyChat.exe`. The managed Host project copies
`bin\Debug\EasyChat.ImeHook.Native.dll` when that native output exists.

An x64 hook observes x64 GUI processes. A separate x86 build is required for
32-bit targets. UIPI, protected/admin processes, browser process boundaries,
TSF-only controls, custom-rendered controls, games, and anti-cheat software can
limit or prevent messages from being observed.
