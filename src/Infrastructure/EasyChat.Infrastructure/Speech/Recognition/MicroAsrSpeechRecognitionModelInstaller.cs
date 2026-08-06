using System.Formats.Tar;
using System.IO.Compression;
using EasyChat.Contracts.Speech;
using MicroASR;

namespace EasyChat.Infrastructure.Speech.Recognition;

public sealed class MicroAsrSpeechRecognitionModelInstaller :
    ISpeechRecognitionModelInstaller,
    ISpeechRecognitionModelRemover
{
    private readonly Func<string> _modelsDirectory;
    private readonly Action? _modelsChanged;
    private readonly SemaphoreSlim _importGate = new(1, 1);

    public MicroAsrSpeechRecognitionModelInstaller(MicroAsrSpeechRecognitionModelCatalog catalog)
        : this(() => catalog.ModelsDirectory, catalog.NotifyModelsChanged)
    {
    }

    internal MicroAsrSpeechRecognitionModelInstaller(string modelsDirectory)
        : this(() => modelsDirectory, null)
    {
    }

    private MicroAsrSpeechRecognitionModelInstaller(
        Func<string> modelsDirectory,
        Action? modelsChanged)
    {
        ArgumentNullException.ThrowIfNull(modelsDirectory);
        _modelsDirectory = modelsDirectory;
        _modelsChanged = modelsChanged;
    }

    private string ModelsDirectory => Path.GetFullPath(_modelsDirectory());

    public async ValueTask<SpeechRecognitionModelImportResult> ImportAsync(
        SpeechRecognitionModelImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SourcePaths);
        if (request.SourcePaths.Count == 0)
            throw new ArgumentException("At least one model source is required.", nameof(request));
        foreach (var sourcePath in request.SourcePaths)
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        await _importGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () => Import(request, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _importGate.Release();
        }
    }

    public async ValueTask<bool> DeleteAsync(
        string modelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        await _importGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var deleted = await Task.Run(() => Delete(modelId, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            if (deleted)
                _modelsChanged?.Invoke();
            return deleted;
        }
        finally
        {
            _importGate.Release();
        }
    }

    private SpeechRecognitionModelImportResult Import(
        SpeechRecognitionModelImportRequest request,
        CancellationToken cancellationToken)
    {
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var sourcePaths = request.SourcePaths
            .Select(Path.GetFullPath)
            .Distinct(pathComparer)
            .ToArray();
        foreach (var sourcePath in sourcePaths)
            ValidateSource(sourcePath, request.SourceKind);

        var modelsDirectory = ModelsDirectory;
        var modelsParent = Directory.GetParent(modelsDirectory)?.FullName
                           ?? throw new InvalidOperationException("The model library has no parent directory.");
        Directory.CreateDirectory(modelsParent);
        var stagingRoot = Path.Combine(
            modelsParent,
            $".{Path.GetFileName(modelsDirectory)}.import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            var scanRoots = sourcePaths.Select((sourcePath, index) => request.SourceKind switch
            {
                SpeechRecognitionModelImportSourceKind.Directory => sourcePath,
                SpeechRecognitionModelImportSourceKind.Archive => ExtractArchive(
                    sourcePath,
                    Path.Combine(
                        stagingRoot,
                        $"archive-{index}",
                        GetArchiveDirectoryName(sourcePath)),
                    cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(request.SourceKind))
            }).ToArray();

            var sharedVadPath = FindSharedVad(scanRoots) ?? FindSharedVad([modelsDirectory]);
            var validationErrors = new List<Exception>();
            var packages = scanRoots
                .SelectMany(scanRoot => DiscoverPackages(
                    scanRoot,
                    sharedVadPath,
                    validationErrors,
                    cancellationToken))
                .DistinctBy(package => package.Directory, pathComparer)
                .ToArray();
            if (packages.Length == 0)
            {
                if (validationErrors.Count > 0)
                {
                    throw new InvalidDataException(
                        $"No compatible MicroASR model was found. {validationErrors[0].Message}",
                        validationErrors[0]);
                }
                throw new InvalidDataException("No compatible MicroASR model was found in the selected source.");
            }

            var result = InstallPackages(packages, stagingRoot, cancellationToken);
            if (result.ImportedModels.Count > 0)
                _modelsChanged?.Invoke();
            return result;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    private bool Delete(string modelId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = ValidateModelIdentifier(modelId);
        var targetDirectory = Path.Combine(ModelsDirectory, id);
        if (!Directory.Exists(targetDirectory))
            return false;
        if (!SpeechModelPackage.IsSupported(targetDirectory))
            throw new IOException($"The installed model directory '{id}' is incomplete or invalid.");

        Directory.Delete(targetDirectory, recursive: true);
        return true;
    }

    private SpeechRecognitionModelImportResult InstallPackages(
        IReadOnlyList<SpeechModelPackage> packages,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ModelsDirectory);
        var preparedRoot = Path.Combine(stagingRoot, "prepared");
        Directory.CreateDirectory(preparedRoot);
        var imported = new List<SpeechRecognitionModel>();
        var skipped = new List<SpeechRecognitionModel>();
        var prepared = new List<(string Id, string Directory)>();
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddSkipped(string id)
        {
            if (skippedIdentifiers.Add(id))
                skipped.Add(new SpeechRecognitionModel(id));
        }

        foreach (var package in packages.OrderBy(item => item.Locale, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = ValidateModelIdentifier(package.Locale);
            if (!identifiers.Add(id))
            {
                AddSkipped(id);
                continue;
            }

            var targetDirectory = Path.Combine(ModelsDirectory, id);
            if (Directory.Exists(targetDirectory))
            {
                if (!SpeechModelPackage.IsSupported(targetDirectory))
                    throw new IOException($"The existing model directory '{id}' is incomplete or invalid.");
                AddSkipped(id);
                continue;
            }
            if (File.Exists(targetDirectory))
                throw new IOException($"A file already occupies the model destination '{id}'.");

            var preparedDirectory = Path.Combine(preparedRoot, id);
            CopyDirectory(package.Directory, preparedDirectory, cancellationToken);
            if (!IsInsideDirectory(package.VadPath, package.Directory))
            {
                File.Copy(
                    package.VadPath,
                    Path.Combine(preparedDirectory, "svad.quantized.onnx"),
                    overwrite: true);
            }
            _ = SpeechModelPackage.Load(preparedDirectory);
            prepared.Add((id, preparedDirectory));
        }

        var movedDirectories = new List<string>();
        try
        {
            foreach (var item in prepared)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var targetDirectory = Path.Combine(ModelsDirectory, item.Id);
                Directory.Move(item.Directory, targetDirectory);
                movedDirectories.Add(targetDirectory);
                imported.Add(new SpeechRecognitionModel(item.Id));
            }
        }
        catch
        {
            foreach (var directory in movedDirectories.AsEnumerable().Reverse())
                TryDeleteDirectory(directory);
            throw;
        }

        return new SpeechRecognitionModelImportResult(imported, skipped);
    }

    private static IReadOnlyList<SpeechModelPackage> DiscoverPackages(
        string scanRoot,
        string? fallbackVadPath,
        ICollection<Exception> validationErrors,
        CancellationToken cancellationToken)
    {
        if (TryLoadPackage(scanRoot, fallbackVadPath, out var selectedPackage, out var validationError))
            return [selectedPackage!];
        if (validationError is not null)
            validationErrors.Add(validationError);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };
        var packages = new List<SpeechModelPackage>();
        foreach (var directory in Directory.EnumerateDirectories(scanRoot, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryLoadPackage(directory, fallbackVadPath, out var package, out validationError))
                packages.Add(package!);
            else if (validationError is not null)
                validationErrors.Add(validationError);
        }
        return packages;
    }

    private static bool TryLoadPackage(
        string directory,
        string? fallbackVadPath,
        out SpeechModelPackage? package,
        out Exception? validationError)
    {
        validationError = null;
        if (!File.Exists(Path.Combine(directory, "model_onnx_quant.config")) ||
            !File.Exists(Path.Combine(directory, "sr.ini")))
        {
            package = null;
            return false;
        }

        try
        {
            package = SpeechModelPackage.Load(directory, fallbackVadPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           InvalidDataException or NotSupportedException or
                                           InvalidOperationException or ArgumentException)
        {
            package = null;
            validationError = exception;
            return false;
        }
    }

    private static string? FindSharedVad(IEnumerable<string> searchRoots)
    {
        foreach (var searchRoot in searchRoots)
        {
            if (!Directory.Exists(searchRoot))
                continue;

            var direct = Path.Combine(searchRoot, "svad.quantized.onnx");
            if (File.Exists(direct))
                return direct;

            // Upstream places the shared, locale-neutral VAD only in the en-US model archive.
            var english = Path.Combine(searchRoot, "en-US", "svad.quantized.onnx");
            if (File.Exists(english))
                return english;

            var shared = Directory.EnumerateFiles(
                    searchRoot,
                    "svad.quantized.onnx",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    })
                .FirstOrDefault();
            if (shared is not null)
                return shared;
        }
        return null;
    }

    private static string ExtractArchive(
        string archivePath,
        string destination,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ExtractZip(archivePath, destination, cancellationToken);
            return destination;
        }
        if (archivePath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ||
            archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            ExtractTar(archivePath, destination, cancellationToken);
            return destination;
        }
        throw new NotSupportedException("Only ZIP, TAR, TAR.GZ and TGZ model archives are supported.");
    }

    private static string GetArchiveDirectoryName(string archivePath)
    {
        var fileName = Path.GetFileName(archivePath);
        var directoryName = fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^7]
            : Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(directoryName) || directoryName is "." or "..")
            throw new InvalidDataException("The model archive must have a valid file name.");
        return directoryName;
    }

    private static void ExtractZip(
        string archivePath,
        string destination,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.FullName))
                continue;
            var target = ResolveArchiveEntryPath(destination, entry.FullName);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static void ExtractTar(
        string archivePath,
        string destination,
        CancellationToken cancellationToken)
    {
        using var archiveStream = File.OpenRead(archivePath);
        var compressed = archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                         archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);
        using Stream content = compressed
            ? new GZipStream(archiveStream, CompressionMode.Decompress)
            : archiveStream;
        using var reader = new TarReader(content);
        while (reader.GetNextEntry() is { } entry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = ResolveArchiveEntryPath(destination, entry.Name);
            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(target);
                continue;
            }
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                throw new InvalidDataException($"Unsupported TAR entry type: {entry.EntryType}.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            entry.DataStream?.CopyTo(output);
        }
    }

    private static string ResolveArchiveEntryPath(string destination, string entryName)
    {
        var root = Path.GetFullPath(destination);
        var normalized = entryName
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var target = Path.GetFullPath(Path.Combine(root, normalized));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!string.Equals(target, root, comparison) && !target.StartsWith(rootPrefix, comparison))
            throw new InvalidDataException($"Archive entry escapes the extraction directory: {entryName}");
        return target;
    }

    private static void CopyDirectory(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Symbolic-link model directories are not supported.");

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private static string ValidateModelIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) ||
            identifier is "." or ".." ||
            !string.Equals(Path.GetFileName(identifier), identifier, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Invalid model directory name: '{identifier}'.");
        }
        return identifier;
    }

    private static bool IsInsideDirectory(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative) &&
               relative != ".." &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static void ValidateSource(
        string sourcePath,
        SpeechRecognitionModelImportSourceKind sourceKind)
    {
        if (sourceKind == SpeechRecognitionModelImportSourceKind.Directory && !Directory.Exists(sourcePath))
            throw new DirectoryNotFoundException($"Model source directory was not found: {sourcePath}");
        if (sourceKind == SpeechRecognitionModelImportSourceKind.Archive && !File.Exists(sourcePath))
            throw new FileNotFoundException("Model archive was not found.", sourcePath);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
        }
    }
}
