using EasyChat.Shared.Results;

namespace EasyChat.Infrastructure.MacOS;

/// <summary>
/// Registration state macOS reports for the application's login item.
/// </summary>
internal enum MacLoginItemState
{
    /// <summary>Registered and allowed to launch at login.</summary>
    Enabled,

    /// <summary>Not registered, which includes a registration the user switched off.</summary>
    NotRegistered,

    /// <summary>Registered, but macOS still needs the user to approve it in System Settings.</summary>
    RequiresApproval,

    /// <summary>macOS cannot find a launchable bundle, typically outside a signed <c>.app</c>.</summary>
    NotFound
}

/// <summary>
/// The seam between the auto-start adapter and <c>SMAppService</c>, so the state mapping can be
/// exercised without registering a real login item on the developer's machine.
/// </summary>
internal interface IMacLoginItemGateway
{
    MacLoginItemState Read();

    Result Register();

    Result Unregister();
}
