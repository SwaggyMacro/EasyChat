using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Native;
using EasyChat.Shared.Results;

namespace EasyChat.Infrastructure.MacOS.Input;

/// <summary>
/// Resolves and restores the application EasyChat is working against.
/// </summary>
/// <remarks>
/// macOS has no per-window foreground concept the way Win32 does: activation happens per
/// application. A target therefore names the application, and the focused target additionally goes
/// through the accessibility API, which is the only public way to learn which application owns the
/// keyboard focus. Without Accessibility approval that query fails rather than falling back to the
/// frontmost application, because the two differ exactly when it matters.
/// </remarks>
public sealed class MacWindowFocus : IWindowFocus
{
    private static readonly TimeSpan ActivationPollInterval = TimeSpan.FromMilliseconds(50);
    private const int ActivationAttempts = 10;

    public ValueTask<Result<ExternalTargetToken>> GetForegroundTargetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            EnsureSupportedHost();
            using var pool = AutoreleasePool.Push();
            var application = WorkspaceNative.FrontmostApplication();
            var processIdentifier = WorkspaceNative.ProcessIdentifier(application);
            return ValueTask.FromResult(processIdentifier > 0
                ? Result<ExternalTargetToken>.Success(
                    MacTargetTokens.FromTarget(new MacTarget(processIdentifier, 0)))
                : Result<ExternalTargetToken>.Failure(new Error(
                    "window.foreground-unavailable",
                    "macOS reported no frontmost application.")));
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(Result<ExternalTargetToken>.Failure(
                new Error("window.foreground-failed", exception.Message)));
        }
    }

    public ValueTask<Result<ExternalTargetToken>> GetFocusedTargetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            EnsureSupportedHost();
            if (!AccessibilityNative.IsProcessTrusted())
            {
                return ValueTask.FromResult(Result<ExternalTargetToken>.Failure(new Error(
                    "window.focus-permission-required",
                    "Reading the focused application needs Accessibility access.")));
            }

            return ValueTask.FromResult(ReadFocusedTarget());
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(Result<ExternalTargetToken>.Failure(
                new Error("window.focus-failed", exception.Message)));
        }
    }

    public async ValueTask<Result> EnsureFocusedAsync(
        ExternalTargetToken target,
        CancellationToken cancellationToken = default)
    {
        if (!MacTargetTokens.TryDecode(target, out var decoded))
        {
            return Result.Failure(new Error(
                "window.target-invalid",
                "The target token was not issued by this macOS session."));
        }

        try
        {
            EnsureSupportedHost();
            for (var attempt = 0; attempt < ActivationAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outcome = TryActivate(decoded);
                if (outcome is { } failure)
                    return failure;

                if (IsFrontmost(decoded.ProcessIdentifier))
                    return Result.Success();

                await Task.Delay(ActivationPollInterval, cancellationToken).ConfigureAwait(false);
            }

            return IsFrontmost(decoded.ProcessIdentifier)
                ? Result.Success()
                : Result.Failure(new Error(
                    "window.focus-failed",
                    "The target application did not come to the front."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result.Failure(new Error("window.focus-failed", exception.Message));
        }
    }

    /// <summary>
    /// Not supported on macOS, and deliberately so: this port identifies another application's
    /// target, and EasyChat does not restyle windows it does not own. EasyChat's own floating
    /// windows are configured through the presentation window behaviour port instead.
    /// </summary>
    public ValueTask<Result> ConfigureNoActivateAsync(
        ExternalTargetToken target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Result.Failure(new Error(
            "window.no-activate-unsupported",
            "macOS configures EasyChat's own windows through the window behaviour port, not by target token.")));
    }

    private static Result<ExternalTargetToken> ReadFocusedTarget()
    {
        var systemWide = AccessibilityNative.AXUIElementCreateSystemWide();
        if (systemWide == IntPtr.Zero)
        {
            return Result<ExternalTargetToken>.Failure(new Error(
                "window.focus-unavailable",
                "The accessibility system element is unavailable."));
        }

        var application = IntPtr.Zero;
        try
        {
            application = AccessibilityNative.CopyAttribute(
                systemWide,
                AccessibilityNative.FocusedApplicationAttribute);
            if (application == IntPtr.Zero)
            {
                return Result<ExternalTargetToken>.Failure(new Error(
                    "window.focus-unavailable",
                    "macOS reported no focused application."));
            }

            return AccessibilityNative.AXUIElementGetPid(application, out var processIdentifier)
                   == AccessibilityNative.Success
                   && processIdentifier > 0
                ? Result<ExternalTargetToken>.Success(
                    MacTargetTokens.FromTarget(new MacTarget(processIdentifier, 0)))
                : Result<ExternalTargetToken>.Failure(new Error(
                    "window.focus-unavailable",
                    "The focused application did not report a process."));
        }
        finally
        {
            if (application != IntPtr.Zero)
                CoreFoundationNative.CFRelease(application);
            CoreFoundationNative.CFRelease(systemWide);
        }
    }

    /// <summary>Answers a failure when the target is gone, otherwise null after asking to activate.</summary>
    private static Result? TryActivate(MacTarget target)
    {
        using var pool = AutoreleasePool.Push();
        var application = WorkspaceNative.ApplicationWithProcessIdentifier(target.ProcessIdentifier);
        if (application == IntPtr.Zero || WorkspaceNative.IsTerminated(application))
        {
            return Result.Failure(new Error(
                "window.target-exited",
                "The target application is no longer running."));
        }

        WorkspaceNative.Activate(application);
        return null;
    }

    private static bool IsFrontmost(int processIdentifier)
    {
        using var pool = AutoreleasePool.Push();
        return WorkspaceNative.ProcessIdentifier(WorkspaceNative.FrontmostApplication())
               == processIdentifier;
    }

    private static void EnsureSupportedHost()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
            throw new PlatformNotSupportedException("Window focus requires macOS 26 or later.");
    }
}
