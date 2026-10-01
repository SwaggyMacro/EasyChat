using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.MacOS.Native;
using EasyChat.Shared.Results;

namespace EasyChat.Infrastructure.MacOS.Input;

/// <summary>
/// Enumerates interactive applications and resolves a target token to its owning application.
/// </summary>
/// <remarks>
/// The persisted identity is the bundle identifier, which is stable across launches and across
/// machines, unlike the process id encoded in <see cref="ExternalTargetToken"/>. Enumeration filters
/// on <c>NSApplicationActivationPolicyRegular</c>: that is the set of applications with a Dock icon
/// and a user interface, which is the closest public answer to "has a visible window" that does not
/// require Screen Recording approval just to populate a picker. Window titles are left unset for the
/// same reason — reading them needs Screen Recording, and listing applications must not.
/// </remarks>
internal sealed class MacRunningProcessCatalog : IRunningProcessCatalog
{
    public ValueTask<IReadOnlyList<RunningProcessDescriptor>> GetRunningProcessesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSupportedHost();

        using var pool = AutoreleasePool.Push();
        var applications = WorkspaceNative.RunningApplications();
        var count = FoundationNative.CountOf(applications);
        var descriptors = new List<RunningProcessDescriptor>((int)count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var self = Environment.ProcessId;

        for (nint index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var application = FoundationNative.ItemAt(applications, index);
            if (WorkspaceNative.Policy(application) != ActivationPolicy.Regular)
                continue;
            if (WorkspaceNative.ProcessIdentifier(application) == self)
                continue;

            var identifier = Identify(application);
            if (identifier is null || !seen.Add(identifier))
                continue;

            descriptors.Add(new RunningProcessDescriptor(
                identifier,
                WorkspaceNative.LocalizedName(application) ?? identifier,
                BundleNative.ReadInfoString(
                    WorkspaceNative.BundleUrl(application),
                    "CFBundleGetInfoString",
                    "CFBundleShortVersionString"),
                WindowTitle: null,
                AppIconNative.ReadPng(WorkspaceNative.Icon(application)) ?? ReadOnlyMemory<byte>.Empty));
        }

        return ValueTask.FromResult<IReadOnlyList<RunningProcessDescriptor>>(descriptors);
    }

    public ValueTask<Result<string>> ResolveProcessIdentifierAsync(
        ExternalTargetToken target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (target.IsEmpty)
        {
            return ValueTask.FromResult(Result<string>.Failure(new Error(
                "running-process.empty-target",
                "The target token is empty.")));
        }

        if (!MacTargetTokens.TryDecode(target, out var decoded))
        {
            return ValueTask.FromResult(Result<string>.Failure(new Error(
                "running-process.invalid-target",
                "The target token was not issued by this macOS session.")));
        }

        try
        {
            EnsureSupportedHost();
            using var pool = AutoreleasePool.Push();
            var application =
                WorkspaceNative.ApplicationWithProcessIdentifier(decoded.ProcessIdentifier);
            if (application == IntPtr.Zero || WorkspaceNative.IsTerminated(application))
            {
                return ValueTask.FromResult(Result<string>.Failure(new Error(
                    "running-process.unavailable",
                    "The target application is no longer running.")));
            }

            var identifier = Identify(application);
            return ValueTask.FromResult(identifier is null
                ? Result<string>.Failure(new Error(
                    "running-process.unavailable",
                    "Unable to resolve the identity of the target application."))
                : Result<string>.Success(identifier));
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(Result<string>.Failure(
                new Error("running-process.unavailable", exception.Message)));
        }
    }

    /// <summary>
    /// The bundle identifier, or the localized name for the rare bundle-less application, so that a
    /// selection allow/deny entry the user saved keeps matching after a restart.
    /// </summary>
    private static string? Identify(IntPtr application)
    {
        var bundleIdentifier = WorkspaceNative.BundleIdentifier(application);
        if (!string.IsNullOrWhiteSpace(bundleIdentifier))
            return bundleIdentifier;

        var name = WorkspaceNative.LocalizedName(application);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static void EnsureSupportedHost()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(26))
            throw new PlatformNotSupportedException(
                "Running application discovery requires macOS 26 or later.");
    }
}
