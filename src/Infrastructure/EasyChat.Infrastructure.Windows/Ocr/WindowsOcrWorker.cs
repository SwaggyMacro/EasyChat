using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using EasyChat.Contracts.ApplicationData;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.Windows.Workers;
using OpenCvSharp;

namespace EasyChat.Infrastructure.Windows.Ocr;

[SupportedOSPlatform("windows")]
internal interface IWindowsOcrWorkerClient : IDisposable
{
    IReadOnlyList<WindowsOcrBackendRegion> Recognize(
        ImageFrame image,
        WindowsOcrLanguageSelection language,
        string modelDirectory,
        bool enableRotation,
        CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsOcrWorkerClient : IWindowsOcrWorkerClient
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RecognitionTimeout = TimeSpan.FromMinutes(2);

    private readonly object _gate = new();
    private readonly bool _persistent;
    private readonly Process _process;
    private readonly NamedPipeClientStream _pipe;
    private readonly BinaryReader _reader;
    private readonly BinaryWriter _writer;
    private bool _disposed;

    internal WindowsOcrWorkerClient(bool persistent)
    {
        _persistent = persistent;
        var pipeName = "EasyChat.Ocr." + Guid.NewGuid().ToString("N");
        _process = WindowsWorkerProcess.Start(
            "--ocr-worker",
            persistent ? [pipeName, "--persistent"] : [pipeName]);
        _pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            _pipe.ConnectAsync()
                .WaitAsync(ConnectionTimeout)
                .GetAwaiter()
                .GetResult();
            _reader = new BinaryReader(_pipe, Encoding.UTF8, leaveOpen: true);
            _writer = new BinaryWriter(_pipe, Encoding.UTF8, leaveOpen: true);
        }
        catch
        {
            _pipe.Dispose();
            WindowsWorkerProcess.TryTerminate(_process);
            _process.Dispose();
            throw;
        }
    }

    public IReadOnlyList<WindowsOcrBackendRegion> Recognize(
        ImageFrame image,
        WindowsOcrLanguageSelection language,
        string modelDirectory,
        bool enableRotation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_process.HasExited)
                throw new InvalidOperationException("OCR worker exited unexpectedly.");

            using var cancellationRegistration = cancellationToken.Register(
                static state => WindowsWorkerProcess.TryTerminate((Process)state!),
                _process);
            try
            {
                OcrWorkerProtocol.WriteRequest(
                    _writer,
                    new OcrWorkerRequest(
                        Path.GetFullPath(modelDirectory),
                        language.Language.Id,
                        enableRotation,
                        image));

                var response = Task.Run(() => OcrWorkerProtocol.ReadResponse(_reader))
                    .WaitAsync(RecognitionTimeout, cancellationToken)
                    .GetAwaiter()
                    .GetResult();
                return response.Status switch
                {
                    OcrWorkerStatus.Success => response.Regions,
                    OcrWorkerStatus.ModelNotDownloaded =>
                        throw new OcrModelNotDownloadedException(language.Language),
                    OcrWorkerStatus.Unsupported =>
                        throw new NotSupportedException(response.ErrorMessage),
                    _ => throw new InvalidOperationException(
                        $"OCR worker failed: {response.ErrorMessage}")
                };
            }
            catch (OperationCanceledException)
            {
                WindowsWorkerProcess.TryTerminate(_process);
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
            catch (TimeoutException exception)
            {
                WindowsWorkerProcess.TryTerminate(_process);
                throw new TimeoutException("OCR worker did not complete in time.", exception);
            }
            catch (IOException)
            {
                WindowsWorkerProcess.TryTerminate(_process);
                throw;
            }
            catch (InvalidDataException)
            {
                WindowsWorkerProcess.TryTerminate(_process);
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _writer.Dispose();
            _reader.Dispose();
            _pipe.Dispose();
            if (!WindowsWorkerProcess.TryWaitForExit(_process, milliseconds: 5000))
                WindowsWorkerProcess.TryTerminate(_process);
            _process.Dispose();
        }
    }
}

[SupportedOSPlatform("windows")]
public static class WindowsOcrWorker
{
    public static void Run(string pipeName, bool persistent = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.None);
        server.WaitForConnection();
        var reader = new BinaryReader(server, Encoding.UTF8, leaveOpen: true);
        var writer = new BinaryWriter(server, Encoding.UTF8, leaveOpen: true);

        OpenVinoWindowsOcrBackend? backend = null;
        string? activeModelDirectory = null;
        byte[]? imageBuffer = null;
        try
        {
            while (true)
            {
                OcrWorkerRequest request;
                try
                {
                    request = OcrWorkerProtocol.ReadRequest(reader, ref imageBuffer);
                }
                catch (EndOfStreamException)
                {
                    return;
                }
                catch (IOException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                var response = Recognize(request, ref backend, ref activeModelDirectory);
                try
                {
                    OcrWorkerProtocol.WriteResponse(writer, response);
                }
                catch (IOException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (!persistent)
                    return;
            }
        }
        finally
        {
            backend?.Dispose();
            TryDispose(writer);
            reader.Dispose();
        }
    }

    private static void TryDispose(BinaryWriter writer)
    {
        try
        {
            writer.Dispose();
        }
        catch (IOException)
        {
            // The client disconnected before the final flush.
        }
        catch (ObjectDisposedException)
        {
            // The client disconnected before the final flush.
        }
    }

    private static OcrWorkerResponse Recognize(
        OcrWorkerRequest request,
        ref OpenVinoWindowsOcrBackend? backend,
        ref string? activeModelDirectory)
    {
        try
        {
            if (!OcrLanguages.TryGet(request.LanguageId, out var language))
                throw new NotSupportedException(
                    $"OCR language '{request.LanguageId}' is not supported.");

            var modelDirectory = Path.GetFullPath(request.ModelDirectory);
            if (backend is null)
            {
                activeModelDirectory = modelDirectory;
                var paths = new OcrWorkerApplicationDataPaths(modelDirectory);
                backend = new OpenVinoWindowsOcrBackend(paths, logger: null);
            }
            else if (!string.Equals(
                         activeModelDirectory,
                         modelDirectory,
                         StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "A persistent OCR worker cannot change its model directory.");
            }

            var selection = OpenVinoOcrModelCatalog.ResolveLanguage(language);
            if (!MemoryMarshal.TryGetArray(request.Image.Pixels, out var segment)
                || segment.Array is null
                || segment.Offset != 0)
            {
                throw new InvalidDataException("OCR worker image buffer is not array-backed.");
            }

            using var bgra = Mat.FromPixelData(
                request.Image.Height,
                request.Image.Width,
                MatType.CV_8UC4,
                segment.Array,
                request.Image.Stride);
            using var bgr = new Mat();
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
            var regions = backend.RecognizeLocal(
                bgr,
                selection,
                request.EnableRotation,
                CancellationToken.None);
            GC.KeepAlive(segment.Array);
            return OcrWorkerResponse.Success(regions);
        }
        catch (OcrModelNotDownloadedException exception)
        {
            return OcrWorkerResponse.Failure(
                OcrWorkerStatus.ModelNotDownloaded,
                exception.Message);
        }
        catch (NotSupportedException exception)
        {
            return OcrWorkerResponse.Failure(OcrWorkerStatus.Unsupported, exception.Message);
        }
        catch (Exception exception)
        {
            return OcrWorkerResponse.Failure(OcrWorkerStatus.Failed, exception.Message);
        }
    }

    private sealed class OcrWorkerApplicationDataPaths : IApplicationDataPaths
    {
        internal OcrWorkerApplicationDataPaths(string modelDirectory)
        {
            OcrModelsDirectory = Path.GetFullPath(modelDirectory);
            var modelsDirectory = Directory.GetParent(OcrModelsDirectory)?.FullName;
            var rootDirectory = modelsDirectory is null
                ? OcrModelsDirectory
                : Directory.GetParent(modelsDirectory)?.FullName ?? modelsDirectory;
            Current = new ApplicationDataLocation(rootDirectory, IsDefault: false);
            ConfigurationDirectory = Path.Combine(rootDirectory, "Configuration");
            SpeechModelsDirectory = Path.Combine(rootDirectory, "Models", "ASR");
        }

        public event EventHandler<ApplicationDataLocationChangedEventArgs>? LocationChanged
        {
            add { }
            remove { }
        }

        public ApplicationDataLocation Current { get; }
        public string ConfigurationDirectory { get; }
        public string SpeechModelsDirectory { get; }
        public string OcrModelsDirectory { get; }
    }
}

internal static class OcrWorkerProtocol
{
    private const int Magic = 0x524F4345;
    private const int Version = 2;
    private const int MaxRegionCount = 100_000;
    private const int MaxPolygonPointCount = 10_000;

    internal static void WriteRequest(BinaryWriter writer, OcrWorkerRequest request)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(request);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(request.ModelDirectory);
        writer.Write(request.LanguageId);
        writer.Write(request.EnableRotation);
        WindowsImageFrameProtocol.Write(writer, request.Image);
        writer.Flush();
    }

    internal static OcrWorkerRequest ReadRequest(BinaryReader reader)
    {
        byte[]? imageBuffer = null;
        return ReadRequest(reader, ref imageBuffer);
    }

    internal static OcrWorkerRequest ReadRequest(BinaryReader reader, ref byte[]? imageBuffer)
    {
        ArgumentNullException.ThrowIfNull(reader);
        EnsureHeader(reader);
        var modelDirectory = reader.ReadString();
        var languageId = reader.ReadString();
        var enableRotation = reader.ReadBoolean();
        var image = WindowsImageFrameProtocol.Read(reader, ref imageBuffer);
        return new OcrWorkerRequest(modelDirectory, languageId, enableRotation, image);
    }

    internal static void WriteResponse(BinaryWriter writer, OcrWorkerResponse response)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(response);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write((byte)response.Status);
        if (response.Status != OcrWorkerStatus.Success)
        {
            writer.Write(response.ErrorMessage ?? string.Empty);
            writer.Flush();
            return;
        }

        writer.Write(response.Regions.Count);
        foreach (var region in response.Regions)
        {
            writer.Write(region.Text);
            writer.Write(region.FallbackAngle);
            writer.Write(region.Confidence);
            writer.Write(region.Polygon.Count);
            foreach (var point in region.Polygon)
            {
                writer.Write(point.X);
                writer.Write(point.Y);
            }
        }
        writer.Flush();
    }

    internal static OcrWorkerResponse ReadResponse(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        EnsureHeader(reader);
        var status = (OcrWorkerStatus)reader.ReadByte();
        if (!Enum.IsDefined(status))
            throw new InvalidDataException("OCR worker response status is invalid.");
        if (status != OcrWorkerStatus.Success)
            return OcrWorkerResponse.Failure(status, reader.ReadString());

        var regionCount = reader.ReadInt32();
        if (regionCount < 0 || regionCount > MaxRegionCount)
            throw new InvalidDataException("OCR worker region count is invalid.");
        var regions = new WindowsOcrBackendRegion[regionCount];
        for (var regionIndex = 0; regionIndex < regionCount; regionIndex++)
        {
            var text = reader.ReadString();
            var angle = reader.ReadDouble();
            var confidence = reader.ReadDouble();
            var pointCount = reader.ReadInt32();
            if (pointCount < 0 || pointCount > MaxPolygonPointCount)
                throw new InvalidDataException("OCR worker polygon point count is invalid.");
            var points = new WindowsOcrPoint[pointCount];
            for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
                points[pointIndex] = new WindowsOcrPoint(reader.ReadDouble(), reader.ReadDouble());
            regions[regionIndex] = new WindowsOcrBackendRegion(
                text,
                points,
                angle,
                confidence);
        }
        return OcrWorkerResponse.Success(regions);
    }

    private static void EnsureHeader(BinaryReader reader)
    {
        if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version)
            throw new InvalidDataException("OCR worker protocol header is invalid.");
    }
}

internal sealed record OcrWorkerRequest(
    string ModelDirectory,
    string LanguageId,
    bool EnableRotation,
    ImageFrame Image);

internal enum OcrWorkerStatus : byte
{
    Success = 0,
    ModelNotDownloaded = 1,
    Unsupported = 2,
    Failed = 3
}

internal sealed record OcrWorkerResponse(
    OcrWorkerStatus Status,
    IReadOnlyList<WindowsOcrBackendRegion> Regions,
    string? ErrorMessage)
{
    internal static OcrWorkerResponse Success(IReadOnlyList<WindowsOcrBackendRegion> regions) =>
        new(OcrWorkerStatus.Success, regions, null);

    internal static OcrWorkerResponse Failure(OcrWorkerStatus status, string errorMessage) =>
        new(status, [], errorMessage);
}
