using System.Text.Json;
using EasyChat.Contracts.ApplicationData;
using EasyChat.Shared.Results;

namespace EasyChat.Infrastructure.ApplicationData;

internal sealed class ApplicationDataStore : IApplicationDataStore
{
    private const string ConfigurationDirectoryName = "Configuration";
    private const string SpeechModelsRelativePath = "Models/ASR";
    private const string OcrModelsRelativePath = "Models/OCR";
    private const string ImageTranslationModelsRelativePath = "Models/ImageTranslation";
    private const string LocationFileName = ".data-location.json";
    private const string ApplicationName = "EasyChat";

    private readonly object _sync = new();
    private readonly SemaphoreSlim _changeGate = new(1, 1);
    private readonly string _defaultRootDirectory;
    private readonly string _applicationDirectory;
    private readonly string? _legacyDataRootDirectory;
    private readonly string? _locationFilePath;
    private readonly string? _legacyLocationFilePath;
    private readonly string? _legacyOcrDirectory;
    private readonly bool _migrateLegacyData;
    private string _rootDirectory;
    private string? _configurationDirectoryOverride;

    private ApplicationDataStore(
        string defaultRootDirectory,
        string applicationDirectory,
        bool persistLocation,
        bool migrateLegacyData,
        string? configurationDirectoryOverride = null,
        string? legacyDataRootDirectory = null)
    {
        _defaultRootDirectory = NormalizeDirectory(defaultRootDirectory);
        _applicationDirectory = NormalizeDirectory(applicationDirectory);
        _legacyDataRootDirectory = legacyDataRootDirectory is null
            ? null
            : NormalizeDirectory(legacyDataRootDirectory);
        _locationFilePath = persistLocation
            ? Path.Combine(_defaultRootDirectory, LocationFileName)
            : null;
        _legacyLocationFilePath = _legacyDataRootDirectory is null
            ? null
            : Path.Combine(_legacyDataRootDirectory, LocationFileName);
        _legacyOcrDirectory = migrateLegacyData
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EasyChat",
                "PaddleOcrModels")
            : null;
        _migrateLegacyData = migrateLegacyData;
        _configurationDirectoryOverride = configurationDirectoryOverride is null
            ? null
            : NormalizeDirectory(configurationDirectoryOverride);
        _rootDirectory = LoadRootDirectory();
        MigrateLegacyData();
    }

    public event EventHandler<ApplicationDataLocationChangedEventArgs>? LocationChanged;

    public ApplicationDataLocation Current
    {
        get
        {
            var root = RootDirectory;
            return new ApplicationDataLocation(root, PathsEqual(root, _defaultRootDirectory));
        }
    }

    public string ConfigurationDirectory
    {
        get
        {
            lock (_sync)
            {
                return _configurationDirectoryOverride
                       ?? Path.Combine(_rootDirectory, ConfigurationDirectoryName);
            }
        }
    }

    public string SpeechModelsDirectory =>
        Path.Combine(RootDirectory, SpeechModelsRelativePath);

    public string OcrModelsDirectory =>
        Path.Combine(RootDirectory, OcrModelsRelativePath);

    public string ImageTranslationModelsDirectory =>
        Path.Combine(RootDirectory, ImageTranslationModelsRelativePath);

    private string RootDirectory
    {
        get
        {
            lock (_sync)
                return _rootDirectory;
        }
    }

    internal static ApplicationDataStore CreateDefault()
    {
        var legacyRoot = GetLegacyDefaultRootDirectory();
        var defaultRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ApplicationName);
        return new ApplicationDataStore(
            defaultRoot,
            ApplicationResourceDirectory.Current,
            persistLocation: true,
            migrateLegacyData: true,
            legacyDataRootDirectory: legacyRoot);
    }

    internal static ApplicationDataStore CreateFixed(string configurationDirectory)
    {
        var fullConfigurationDirectory = NormalizeDirectory(configurationDirectory);
        var root = Directory.GetParent(fullConfigurationDirectory)?.FullName
                   ?? throw new ArgumentException(
                       "The configuration directory requires a parent directory.",
                       nameof(configurationDirectory));
        return new ApplicationDataStore(
            root,
            ApplicationResourceDirectory.Current,
            persistLocation: false,
            migrateLegacyData: false,
            configurationDirectoryOverride: fullConfigurationDirectory);
    }

    internal ApplicationDataStore(
        string defaultRootDirectory,
        string applicationDirectory,
        string locationFilePath,
        string legacyOcrDirectory,
        string? legacyDataRootDirectory = null)
    {
        _defaultRootDirectory = NormalizeDirectory(defaultRootDirectory);
        _applicationDirectory = NormalizeDirectory(applicationDirectory);
        _legacyDataRootDirectory = legacyDataRootDirectory is null
            ? null
            : NormalizeDirectory(legacyDataRootDirectory);
        _locationFilePath = Path.GetFullPath(locationFilePath);
        _legacyLocationFilePath = _legacyDataRootDirectory is null
            ? null
            : Path.Combine(_legacyDataRootDirectory, LocationFileName);
        _legacyOcrDirectory = NormalizeDirectory(legacyOcrDirectory);
        _migrateLegacyData = true;
        _configurationDirectoryOverride = null;
        _rootDirectory = LoadRootDirectory();
        MigrateLegacyData();
    }

    public async ValueTask<Result<ApplicationDataLocation>> ChangeLocationAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var targetRoot = NormalizeDirectory(rootDirectory);

        await _changeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = Current;
            if (PathsEqual(previous.RootDirectory, targetRoot))
                return Result<ApplicationDataLocation>.Success(previous);

            var validation = ValidateTarget(previous.RootDirectory, targetRoot);
            if (validation is not null)
                return Result<ApplicationDataLocation>.Failure(validation);

            try
            {
                var sourceConfigurationDirectory = ConfigurationDirectory;
                var sourceSpeechModelsDirectory = SpeechModelsDirectory;
                var sourceOcrModelsDirectory = OcrModelsDirectory;
                var sourceImageTranslationModelsDirectory = ImageTranslationModelsDirectory;
                await Task.Run(
                    () => CopyToNewRoot(
                        sourceConfigurationDirectory,
                        sourceSpeechModelsDirectory,
                        sourceOcrModelsDirectory,
                        sourceImageTranslationModelsDirectory,
                        targetRoot,
                        cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                PersistRootDirectory(targetRoot);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Result<ApplicationDataLocation>.Failure(
                    new Error("application-data.move-failed", exception.Message));
            }

            lock (_sync)
            {
                _rootDirectory = targetRoot;
                _configurationDirectoryOverride = null;
            }

            var current = Current;
            LocationChanged?.Invoke(this, new ApplicationDataLocationChangedEventArgs(previous, current));
            return Result<ApplicationDataLocation>.Success(current);
        }
        finally
        {
            _changeGate.Release();
        }
    }

    private string LoadRootDirectory()
    {
        if (TryLoadRootDirectory(_locationFilePath, out var rootDirectory))
            return rootDirectory;

        if (TryLoadRootDirectory(_legacyLocationFilePath, out rootDirectory))
        {
            try
            {
                PersistRootDirectory(rootDirectory);
            }
            catch (IOException)
            {
                // Keep using the legacy pointer when the new location cannot be written yet.
            }
            catch (UnauthorizedAccessException)
            {
                // Keep using the legacy pointer when the new location cannot be written yet.
            }

            return rootDirectory;
        }

        return _defaultRootDirectory;
    }

    private static bool TryLoadRootDirectory(string? locationFilePath, out string rootDirectory)
    {
        rootDirectory = string.Empty;
        if (locationFilePath is null || !File.Exists(locationFilePath))
            return false;

        var json = File.ReadAllText(locationFilePath);
        var pointer = JsonSerializer.Deserialize<LocationPointer>(json)
                      ?? throw new InvalidDataException("The application data location file is empty.");
        if (string.IsNullOrWhiteSpace(pointer.RootDirectory))
            throw new InvalidDataException("The application data location file has no root directory.");
        rootDirectory = NormalizeDirectory(pointer.RootDirectory);
        return true;
    }

    private void MigrateLegacyData()
    {
        if (!_migrateLegacyData)
            return;

        if (_legacyDataRootDirectory is not null)
        {
            CopyMissingContents(
                Path.Combine(_legacyDataRootDirectory, ConfigurationDirectoryName),
                ConfigurationDirectory);
            CopyMissingContents(
                Path.Combine(_legacyDataRootDirectory, "Models"),
                Path.Combine(RootDirectory, "Models"));
        }

        CopyMissingContents(
            Path.Combine(_applicationDirectory, "Configuration"),
            ConfigurationDirectory);
        CopyMissingContents(
            Path.GetFullPath(Path.Combine(_applicationDirectory, "..", "Configuration")),
            ConfigurationDirectory);
        CopyMissingContents(
            Path.Combine(_applicationDirectory, "Models"),
            SpeechModelsDirectory);

        CopyMissingContents(_legacyOcrDirectory!, OcrModelsDirectory);
    }

    private Error? ValidateTarget(string sourceRoot, string targetRoot)
    {
        if (IsNestedPath(sourceRoot, targetRoot) || IsNestedPath(targetRoot, sourceRoot))
        {
            return new Error(
                "application-data.nested-location",
                "The new data directory cannot contain, or be contained by, the current data directory.");
        }

        if (File.Exists(targetRoot))
        {
            return new Error(
                "application-data.location-is-file",
                "The selected data location is a file.");
        }

        if (Directory.Exists(targetRoot) && Directory.EnumerateFileSystemEntries(targetRoot).Any())
        {
            return new Error(
                "application-data.location-not-empty",
                "Select an empty directory for the application data location.");
        }

        return null;
    }

    private static void CopyToNewRoot(
        string sourceConfigurationDirectory,
        string sourceSpeechModelsDirectory,
        string sourceOcrModelsDirectory,
        string sourceImageTranslationModelsDirectory,
        string targetRoot,
        CancellationToken cancellationToken)
    {
        var targetParent = Directory.GetParent(targetRoot)?.FullName
                           ?? throw new InvalidOperationException(
                               "The application data directory cannot be a drive root.");
        Directory.CreateDirectory(targetParent);

        var stagingRoot = Path.Combine(
            targetParent,
            $".{Path.GetFileName(targetRoot)}.migrate-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stagingRoot);
            CopyDirectory(
                sourceConfigurationDirectory,
                Path.Combine(stagingRoot, ConfigurationDirectoryName),
                cancellationToken);
            CopyDirectory(
                sourceSpeechModelsDirectory,
                Path.Combine(stagingRoot, SpeechModelsRelativePath),
                cancellationToken);
            CopyDirectory(
                sourceOcrModelsDirectory,
                Path.Combine(stagingRoot, OcrModelsRelativePath),
                cancellationToken);
            CopyDirectory(
                sourceImageTranslationModelsDirectory,
                Path.Combine(stagingRoot, ImageTranslationModelsRelativePath),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(targetRoot))
                Directory.Delete(targetRoot);
            Directory.Move(stagingRoot, targetRoot);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    private void PersistRootDirectory(string rootDirectory)
    {
        if (_locationFilePath is null)
            return;

        var directory = Path.GetDirectoryName(_locationFilePath)
                        ?? throw new InvalidOperationException(
                            "The application data location file requires a directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_locationFilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(
                    new LocationPointer(rootDirectory),
                    new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, _locationFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void CopyMissingContents(string source, string destination)
    {
        if (!Directory.Exists(source) || PathsEqual(source, destination))
            return;

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                continue;
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                continue;
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target))
                File.Copy(file, target);
        }
    }

    private static void CopyDirectory(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source))
            return;

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                continue;
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                continue;
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static bool IsNestedPath(string parent, string candidate)
    {
        var parentWithSeparator = NormalizeDirectory(parent) + Path.DirectorySeparatorChar;
        return NormalizeDirectory(candidate).StartsWith(
            parentWithSeparator,
            PathComparison);
    }

    private static bool PathsEqual(string left, string right) => string.Equals(
        NormalizeDirectory(left),
        NormalizeDirectory(right),
        PathComparison);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string GetLegacyDefaultRootDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ApplicationName);

    private sealed record LocationPointer(string RootDirectory);
}
