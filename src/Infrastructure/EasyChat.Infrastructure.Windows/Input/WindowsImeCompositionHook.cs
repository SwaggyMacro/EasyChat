using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EasyChat.Infrastructure.Windows.Input;

[SupportedOSPlatform("windows")]
public static class WindowsImeCompositionHook
{
    private const string NativeLibrary = "EasyChat.ImeHook.Native.dll";
    private static int _started;
    private static int _processExitRegistered;

    public static bool Start()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        if (Volatile.Read(ref _started) != 0)
            return true;

        try
        {
            if (!NativeMethods.StartEasyChatImeHook())
            {
                Debug.WriteLine($"EasyChat IME composition hook failed to start (error {NativeMethods.GetEasyChatImeHookLastError()}).");
                return false;
            }

            Volatile.Write(ref _started, 1);
            if (Interlocked.Exchange(ref _processExitRegistered, 1) == 0)
                AppDomain.CurrentDomain.ProcessExit += StopOnProcessExit;
            Debug.WriteLine("EasyChat IME composition hook started.");
            return true;
        }
        catch (DllNotFoundException exception)
        {
            Debug.WriteLine($"EasyChat IME composition hook DLL was not found: {exception.Message}");
            return false;
        }
        catch (BadImageFormatException exception)
        {
            Debug.WriteLine($"EasyChat IME composition hook architecture is incompatible: {exception.Message}");
            return false;
        }
        catch (EntryPointNotFoundException exception)
        {
            Debug.WriteLine($"EasyChat IME composition hook export is missing: {exception.Message}");
            return false;
        }
    }

    public static void Stop()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0)
            return;

        try
        {
            NativeMethods.StopEasyChatImeHook();
        }
        catch (DllNotFoundException)
        {
        }
        catch (BadImageFormatException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static void StopOnProcessExit(object? sender, EventArgs args) => Stop();

    private static class NativeMethods
    {
        [DllImport(NativeLibrary, EntryPoint = "StartEasyChatImeHook", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool StartEasyChatImeHook();

        [DllImport(NativeLibrary, EntryPoint = "StopEasyChatImeHook", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool StopEasyChatImeHook();

        [DllImport(NativeLibrary, EntryPoint = "GetEasyChatImeHookLastError", CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetEasyChatImeHookLastError();
    }
}
