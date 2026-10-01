using EasyChat.Contracts.Platform;
using EasyChat.Shared.Results;

namespace EasyChat.Infrastructure.MacOS;

/// <summary>
/// Requests macOS privacy approvals. This adapter is the only place allowed to surface a system
/// prompt, and it re-reads the privacy state afterwards: an approval macOS has not applied yet is
/// reported as denied with the reason, never as granted.
/// </summary>
public sealed class MacPlatformPermissionRequester : IPlatformPermissionRequester
{
    private readonly IMacPrivacyGateway _privacy;

    public MacPlatformPermissionRequester()
        : this(new MacSystemPrivacyGateway())
    {
    }

    internal MacPlatformPermissionRequester(IMacPrivacyGateway privacy)
    {
        ArgumentNullException.ThrowIfNull(privacy);
        _privacy = privacy;
    }

    public async ValueTask<Result<PermissionStatus>> RequestAsync(
        PlatformPermission permission,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var current = _privacy.Check(permission);
            if (current is MacPrivacyState.Granted
                or MacPrivacyState.Restricted
                or MacPrivacyState.Unavailable)
            {
                return Result<PermissionStatus>.Success(Map(permission, current));
            }

            await _privacy.PromptAsync(permission, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Result<PermissionStatus>.Success(Map(permission, _privacy.Check(permission)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result<PermissionStatus>.Failure(new Error(
                $"platform.permission.{permission.ToString().ToLowerInvariant()}.failed",
                $"The macOS privacy request for {permission} failed: {exception.Message}"));
        }
    }

    private static PermissionStatus Map(PlatformPermission permission, MacPrivacyState state) =>
        new(
            permission,
            state switch
            {
                MacPrivacyState.Granted => PermissionState.Granted,
                MacPrivacyState.Restricted or MacPrivacyState.Unavailable =>
                    PermissionState.Unsupported,
                _ => PermissionState.Denied
            },
            MacPrivacyReasons.Describe(permission, state));
}
