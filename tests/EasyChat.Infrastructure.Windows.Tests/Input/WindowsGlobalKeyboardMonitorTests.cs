using System.Runtime.Versioning;
using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.Windows.Input;

namespace EasyChat.Infrastructure.Windows.Tests.Input;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsGlobalKeyboardMonitorTests
{
    [TestMethod]
    public void RegistrationUsesKeyboardHookWithoutGlobalHotkeyRegistration()
    {
        var messageThread = new FakeMessageThread();
        var backend = new FakeBackend();
        using var monitor = new WindowsGlobalKeyboardMonitor(messageThread, backend);
        GlobalKeyboardEvent? observed = null;

        using var registration = monitor.Start(keyboardEvent => observed = keyboardEvent);

        Assert.AreEqual(1, backend.InstallCount);
        backend.RaiseKeyDown(0x56);

        Assert.IsNotNull(observed);
        Assert.AreEqual("V", observed!.Key);
        Assert.IsTrue(observed.IsPressed);
        Assert.AreEqual(0, backend.GlobalHotkeyRegistrationCount);

        registration.Dispose();
        Assert.AreEqual(1, backend.UninstallCount);
    }

    private sealed class FakeMessageThread : IWindowsMessageThread
    {
        public void Invoke(Action action) => action();
        public void Dispose() { }
    }

    private sealed class FakeBackend : IWindowsKeyboardHookBackend
    {
        private WindowsKeyboardHookCallback? _callback;
        private ushort _virtualKey;

        public int LastError => 0;
        public int InstallCount { get; private set; }
        public int UninstallCount { get; private set; }
        public int GlobalHotkeyRegistrationCount { get; private set; }

        public IntPtr Install(WindowsKeyboardHookCallback callback)
        {
            _callback = callback;
            InstallCount++;
            return new IntPtr(InstallCount);
        }

        public bool Uninstall(IntPtr hook)
        {
            UninstallCount++;
            return true;
        }

        public NativeKeyboardHookEvent ReadEvent(IntPtr data) =>
            new(_virtualKey, 0, 0, 0, UIntPtr.Zero);

        public IntPtr CallNext(IntPtr hook, int code, IntPtr message, IntPtr data) => IntPtr.Zero;

        public void RaiseKeyDown(ushort virtualKey)
        {
            _virtualKey = virtualKey;
            _callback?.Invoke(0, (IntPtr)0x0100, IntPtr.Zero);
        }
    }
}
