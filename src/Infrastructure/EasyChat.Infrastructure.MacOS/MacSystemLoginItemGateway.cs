using EasyChat.Infrastructure.MacOS.Native;
using EasyChat.Shared.Results;

namespace EasyChat.Infrastructure.MacOS;

/// <summary>
/// Drives <c>SMAppService.mainApp</c>. macOS owns the launch entry, so no <c>LaunchAgents</c> plist
/// is written and no helper process is installed.
/// </summary>
internal sealed class MacSystemLoginItemGateway : IMacLoginItemGateway
{
    public MacLoginItemState Read()
    {
        EnsureSupportedHost();
        return ServiceManagementNative.ReadStatus() switch
        {
            LoginItemStatus.Enabled => MacLoginItemState.Enabled,
            LoginItemStatus.RequiresApproval => MacLoginItemState.RequiresApproval,
            LoginItemStatus.NotFound => MacLoginItemState.NotFound,
            _ => MacLoginItemState.NotRegistered
        };
    }

    public Result Register()
    {
        EnsureSupportedHost();
        return ServiceManagementNative.Register(out var error)
            ? Result.Success()
            : Result.Failure(new Error(
                "autostart.register-failed",
                error ?? "macOS refused to register EasyChat as a login item."));
    }

    public Result Unregister()
    {
        EnsureSupportedHost();
        return ServiceManagementNative.Unregister(out var error)
            ? Result.Success()
            : Result.Failure(new Error(
                "autostart.unregister-failed",
                error ?? "macOS refused to remove the EasyChat login item."));
    }

    private static void EnsureSupportedHost()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
            throw new PlatformNotSupportedException(
                "Login item registration requires macOS 26 or later.");
    }
}
