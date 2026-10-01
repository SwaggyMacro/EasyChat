using EasyChat.Contracts.Platform;

namespace EasyChat.Infrastructure.MacOS;

/// <summary>
/// Raw macOS privacy (TCC) answer for one permission, before any EasyChat policy is applied.
/// </summary>
internal enum MacPrivacyState
{
    /// <summary>The user has approved EasyChat and macOS has applied the approval.</summary>
    Granted,

    /// <summary>macOS has not asked the user yet, or has not applied a fresh approval yet.</summary>
    NotDetermined,

    /// <summary>The user has refused, or removed, the approval.</summary>
    Denied,

    /// <summary>System policy withholds the approval, so the user cannot grant it.</summary>
    Restricted,

    /// <summary>macOS exposes no privacy entry point for this permission.</summary>
    Unavailable
}

/// <summary>
/// The single seam between the macOS permission adapters and TCC. It reports raw privacy state and
/// shows system prompts; deciding what a state means for EasyChat belongs to the adapters, so that
/// policy stays testable without a real privacy database. No native handle crosses this interface.
/// </summary>
internal interface IMacPrivacyGateway
{
    /// <summary>Reads the current answer without ever showing a system prompt.</summary>
    MacPrivacyState Check(PlatformPermission permission);

    /// <summary>
    /// Shows the system prompt, or opens the matching System Settings pane, and reports the answer
    /// macOS returns straight away. Callers must still re-check, because macOS applies some
    /// approvals only after the application is relaunched.
    /// </summary>
    ValueTask<MacPrivacyState> PromptAsync(
        PlatformPermission permission,
        CancellationToken cancellationToken);
}
