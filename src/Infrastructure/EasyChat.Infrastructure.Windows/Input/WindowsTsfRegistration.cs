using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using EasyChat.Contracts.Platform;
using EasyChat.Shared.Results;
using Microsoft.Extensions.Logging;

namespace EasyChat.Infrastructure.Windows.Input;

public interface IWindowsTsfRegistration
{
    TextServicesFrameworkStatus Status { get; }
    ValueTask<Result> EnsureRegisteredAsync(CancellationToken cancellationToken = default);
    ValueTask<Result> UnregisterAsync(CancellationToken cancellationToken = default);
}

public sealed class WindowsTsfRegistration(ILogger<WindowsTsfRegistration> logger) : IWindowsTsfRegistration
{
    private const string NativeDllName = "EasyChat.Tsf.Native.dll";
    private const string TsfClsid = "{A1B2C3D4-E5F6-47A8-91B2-C3D4E5F60718}";
    private const string TsfProfileKey = "Software\\Microsoft\\CTF\\TIP\\" + TsfClsid;
    private const string ComServerKey = "Software\\Classes\\CLSID\\" + TsfClsid + "\\InprocServer32";
    private readonly ILogger<WindowsTsfRegistration> _logger = logger;
    private TextServicesFrameworkStatus _status = new(
        TextServicesFrameworkState.NotActive,
        "The EasyChat TSF profile has not been registered yet.");

    public TextServicesFrameworkStatus Status => _status;

    public ValueTask<Result> EnsureRegisteredAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(AppContext.BaseDirectory, NativeDllName);
        if (!File.Exists(path))
        {
            _status = new(
                TextServicesFrameworkState.RegistrationFailed,
                $"The native TSF component was not found: {path}");
            return ValueTask.FromResult(Result.Failure(new Error("tsf.native-missing", _status.Message!)));
        }

        try
        {
            var hr = DllRegisterServer();
            if (hr < 0)
            {
                // TSF's machine-wide profile catalog is normally provisioned by MSI and
                // cannot be rewritten by a standard user at startup. Preserve that
                // installation and repair only the per-user COM registration when needed.
                if (IsMachineProfileRegistered() && EnsureCurrentUserComRegistration(path))
                {
                    TryActivateProfile();
                    _status = new(
                        TextServicesFrameworkState.NotActive,
                        "TSF is registered. Select EasyChat Translate in the Windows input method list.");
                    return ValueTask.FromResult(Result.Success());
                }

                _status = new(TextServicesFrameworkState.RegistrationFailed, $"TSF registration failed (0x{hr:X8}).");
                return ValueTask.FromResult(Result.Failure(new Error("tsf.registration-failed", _status.Message!)));
            }

            TryActivateProfile();
            _status = new(
                TextServicesFrameworkState.NotActive,
                "TSF is registered. Select EasyChat Translate in the Windows input method list.");
            return ValueTask.FromResult(Result.Success());
        }
        catch (DllNotFoundException exception)
        {
            _logger.LogWarning(exception, "Unable to load the EasyChat TSF component.");
            _status = new(TextServicesFrameworkState.RegistrationFailed, exception.Message);
            return ValueTask.FromResult(Result.Failure(new Error("tsf.registration-load-failed", exception.Message)));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to register the EasyChat TSF component.");
            _status = new(TextServicesFrameworkState.RegistrationFailed, exception.Message);
            return ValueTask.FromResult(Result.Failure(new Error("tsf.registration-failed", exception.Message)));
        }
    }

    public ValueTask<Result> UnregisterAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, NativeDllName)))
            return ValueTask.FromResult(Result.Success());

        try
        {
            var hr = DllUnregisterServer();
            if (hr < 0)
                return ValueTask.FromResult(Result.Failure(new Error("tsf.unregistration-failed", $"TSF unregistration failed (0x{hr:X8}).")));
            _status = new(TextServicesFrameworkState.Unsupported, "TSF is not registered.");
            return ValueTask.FromResult(Result.Success());
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to unregister the EasyChat TSF component.");
            return ValueTask.FromResult(Result.Failure(new Error("tsf.unregistration-failed", exception.Message)));
        }
    }

    private static bool IsMachineProfileRegistered()
    {
        using var key = Registry.LocalMachine.OpenSubKey(TsfProfileKey, writable: false);
        return key is not null;
    }

    private static bool EnsureCurrentUserComRegistration(string path)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(ComServerKey, writable: true);
            if (key is null)
                return false;
            key.SetValue(string.Empty, path, RegistryValueKind.String);
            key.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
            return true;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void TryActivateProfile()
    {
        try
        {
            var hr = DllActivateProfile();
            if (hr < 0)
                _logger.LogInformation("TSF profile activation was not granted (0x{HResult:X8}); the user can activate it from Windows input settings.", hr);
        }
        catch (EntryPointNotFoundException exception)
        {
            _logger.LogInformation(exception, "The native TSF component does not expose profile activation yet.");
        }
    }

    [DllImport(NativeDllName, EntryPoint = "DllRegisterServer", CallingConvention = CallingConvention.StdCall)]
    private static extern int DllRegisterServer();

    [DllImport(NativeDllName, EntryPoint = "DllUnregisterServer", CallingConvention = CallingConvention.StdCall)]
    private static extern int DllUnregisterServer();

    [DllImport(NativeDllName, EntryPoint = "DllActivateProfile", CallingConvention = CallingConvention.StdCall)]
    private static extern int DllActivateProfile();
}
