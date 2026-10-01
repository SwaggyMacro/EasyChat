using EasyChat.Contracts.Platform;
using EasyChat.Shared.Results;

namespace EasyChat.Infrastructure.MacOS.ApplicationStartup;

/// <summary>
/// Auto-start backed by <c>SMAppService.mainApp</c>. The registration state is always read back from
/// macOS rather than cached, so a login item the user switches off in System Settings is reported as
/// disabled, and a registration macOS has not approved yet is reported as a failure instead of a
/// silent success.
/// </summary>
internal sealed class MacApplicationAutoStartService : IApplicationAutoStartService
{
    private const string ApprovalPane = "System Settings > General > Login Items & Extensions";

    private readonly IMacLoginItemGateway _loginItem;

    public MacApplicationAutoStartService()
        : this(new MacSystemLoginItemGateway())
    {
    }

    internal MacApplicationAutoStartService(IMacLoginItemGateway loginItem)
    {
        ArgumentNullException.ThrowIfNull(loginItem);
        _loginItem = loginItem;
    }

    public Result<bool> GetEnabled()
    {
        MacLoginItemState state;
        try
        {
            state = _loginItem.Read();
        }
        catch (Exception exception)
        {
            return Result<bool>.Failure(new Error("autostart.read-failed", exception.Message));
        }

        return state switch
        {
            MacLoginItemState.Enabled => true,
            MacLoginItemState.NotFound => Result<bool>.Failure(new Error(
                "autostart.bundle-not-found",
                "macOS cannot find a launchable EasyChat bundle. Move EasyChat.app into " +
                "Applications and start it from there.")),
            _ => false
        };
    }

    public Result SetEnabled(bool enabled)
    {
        try
        {
            var change = enabled ? _loginItem.Register() : _loginItem.Unregister();
            if (change.IsFailure)
                return change;

            return Verify(enabled, _loginItem.Read());
        }
        catch (Exception exception)
        {
            return Result.Failure(new Error("autostart.write-failed", exception.Message));
        }
    }

    private static Result Verify(bool enabled, MacLoginItemState state)
    {
        if (enabled)
        {
            return state switch
            {
                MacLoginItemState.Enabled => Result.Success(),
                MacLoginItemState.RequiresApproval => Result.Failure(new Error(
                    "autostart.requires-approval",
                    $"EasyChat was registered as a login item, but macOS needs your approval in {ApprovalPane} before it can start automatically.")),
                MacLoginItemState.NotFound => Result.Failure(new Error(
                    "autostart.bundle-not-found",
                    "macOS cannot find a launchable EasyChat bundle. Move EasyChat.app into " +
                    "Applications and start it from there.")),
                _ => Result.Failure(new Error(
                    "autostart.register-not-effective",
                    $"macOS did not keep the EasyChat login item. Check {ApprovalPane}."))
            };
        }

        return state == MacLoginItemState.Enabled
            ? Result.Failure(new Error(
                "autostart.unregister-not-effective",
                $"The EasyChat login item is still enabled. Remove it in {ApprovalPane}."))
            : Result.Success();
    }
}
