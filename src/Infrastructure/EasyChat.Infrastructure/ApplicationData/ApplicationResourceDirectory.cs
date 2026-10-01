namespace EasyChat.Infrastructure.ApplicationData;

/// <summary>
/// Locates the read-only files shipped with the application (<c>Assets</c>, bundled <c>Models</c>).
/// They sit beside the executable, except inside an application bundle, where code signing
/// requires everything that is not code to live in <c>Contents/Resources</c> rather than beside the
/// executable in <c>Contents/MacOS</c>.
/// </summary>
internal static class ApplicationResourceDirectory
{
    public static string Current { get; } = Resolve(AppContext.BaseDirectory);

    internal static string Resolve(string executableDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableDirectory);

        var executable = new DirectoryInfo(Path.TrimEndingDirectorySeparator(executableDirectory));
        var contents = executable.Parent;
        if (string.Equals(executable.Name, "MacOS", StringComparison.Ordinal)
            && contents is not null
            && string.Equals(contents.Name, "Contents", StringComparison.Ordinal))
        {
            var resources = Path.Combine(contents.FullName, "Resources");
            if (Directory.Exists(resources))
                return resources;
        }

        return executableDirectory;
    }
}
