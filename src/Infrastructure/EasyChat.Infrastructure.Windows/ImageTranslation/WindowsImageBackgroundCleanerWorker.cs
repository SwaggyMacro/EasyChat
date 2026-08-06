using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.Windows.Workers;
using OpenCvSharp;

namespace EasyChat.Infrastructure.Windows.ImageTranslation;

[SupportedOSPlatform("windows")]
internal static class WindowsImageBackgroundCleanerWorkerClient
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProcessingTimeout = TimeSpan.FromMinutes(2);

    internal static ImageFrame RemoveText(
        ImageFrame source,
        IReadOnlyList<OcrTextRegion> regions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(regions);
        cancellationToken.ThrowIfCancellationRequested();

        var pipeName = "EasyChat.ImageCleaner." + Guid.NewGuid().ToString("N");
        using var process = WindowsWorkerProcess.Start("--image-cleaner-worker", pipeName);
        using var cancellationRegistration = cancellationToken.Register(
            static state => WindowsWorkerProcess.TryTerminate((Process)state!),
            process);
        using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            pipe.ConnectAsync(cancellationToken)
                .WaitAsync(ConnectionTimeout, cancellationToken)
                .GetAwaiter()
                .GetResult();
            using var reader = new BinaryReader(pipe, Encoding.UTF8, leaveOpen: true);
            using var writer = new BinaryWriter(pipe, Encoding.UTF8, leaveOpen: true);
            ImageCleanerWorkerProtocol.WriteRequest(
                writer,
                new ImageCleanerWorkerRequest(source, regions));
            var response = Task.Run(() => ImageCleanerWorkerProtocol.ReadResponse(reader))
                .WaitAsync(ProcessingTimeout, cancellationToken)
                .GetAwaiter()
                .GetResult();
            if (response.Status != ImageCleanerWorkerStatus.Success || response.Image is null)
                throw new InvalidOperationException(
                    $"Image cleaner worker failed: {response.ErrorMessage}");
            return response.Image;
        }
        catch (OperationCanceledException)
        {
            WindowsWorkerProcess.TryTerminate(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (TimeoutException exception)
        {
            WindowsWorkerProcess.TryTerminate(process);
            throw new TimeoutException("Image cleaner worker did not complete in time.", exception);
        }
        finally
        {
            pipe.Dispose();
            if (!WindowsWorkerProcess.TryWaitForExit(process, milliseconds: 5000))
                WindowsWorkerProcess.TryTerminate(process);
        }
    }
}

[SupportedOSPlatform("windows")]
public static class WindowsImageBackgroundCleanerWorker
{
    public static void Run(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.None);
        server.WaitForConnection();
        using var reader = new BinaryReader(server, Encoding.UTF8, leaveOpen: true);
        var writer = new BinaryWriter(server, Encoding.UTF8, leaveOpen: true);

        ImageCleanerWorkerResponse response;
        try
        {
            var request = ImageCleanerWorkerProtocol.ReadRequest(reader);
            response = ImageCleanerWorkerResponse.Success(
                WindowsOpenCvImageBackgroundCleaner.RemoveText(
                    request.Source,
                    request.Regions));
        }
        catch (Exception exception)
        {
            response = ImageCleanerWorkerResponse.Failure(exception.Message);
        }

        try
        {
            ImageCleanerWorkerProtocol.WriteResponse(writer, response);
        }
        catch (IOException)
        {
            // Parent process exited or canceled processing.
        }
        finally
        {
            try
            {
                writer.Dispose();
            }
            catch (IOException)
            {
                // The parent disconnected before the final flush.
            }
            catch (ObjectDisposedException)
            {
                // The parent disconnected before the final flush.
            }
        }
    }
}

internal static class WindowsOpenCvImageBackgroundCleaner
{
    internal static ImageFrame RemoveText(
        ImageFrame source,
        IReadOnlyList<OcrTextRegion> regions)
    {
        if (regions.Count == 0)
            return source;

        var sourcePixels = source.Pixels.ToArray();
        using var bgra = Mat.FromPixelData(
            source.Height,
            source.Width,
            MatType.CV_8UC4,
            sourcePixels,
            source.Stride);
        using var bgr = new Mat();
        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
        GC.KeepAlive(sourcePixels);

        using var mask = new Mat(bgr.Size(), MatType.CV_8UC1, Scalar.All(0));
        foreach (var region in regions)
        {
            var polygon = region.Polygon
                .Select(point => new Point(
                    (int)Math.Round(point.X),
                    (int)Math.Round(point.Y)))
                .ToArray();
            if (polygon.Length >= 3)
                Cv2.FillPoly(mask, [polygon], Scalar.All(255));
        }

        var heights = regions
            .Select(GetHeight)
            .OrderBy(value => value)
            .ToArray();
        var medianHeight = heights[heights.Length / 2];
        var kernelSize = Math.Max(3, (int)Math.Round(medianHeight / 12d) * 2 + 1);
        using var kernel = Cv2.GetStructuringElement(
            MorphShapes.Ellipse,
            new Size(kernelSize, kernelSize));
        Cv2.Dilate(mask, mask, kernel);

        using var inpainted = new Mat();
        Cv2.Inpaint(
            bgr,
            mask,
            inpainted,
            Math.Max(3, medianHeight / 10d),
            InpaintMethod.Telea);

        using var output = new Mat();
        Cv2.CvtColor(inpainted, output, ColorConversionCodes.BGR2BGRA);
        var stride = checked(source.Width * 4);
        var outputPixels = new byte[checked(stride * source.Height)];
        for (var row = 0; row < source.Height; row++)
            Marshal.Copy(output.Data + row * (int)output.Step(), outputPixels, row * stride, stride);

        return new ImageFrame(
            source.Width,
            source.Height,
            stride,
            source.DpiX,
            source.DpiY,
            outputPixels);
    }

    private static double GetHeight(OcrTextRegion region)
    {
        if (region.Polygon.Count == 0)
            return 0;

        var top = region.Polygon.Min(point => point.Y);
        var bottom = region.Polygon.Max(point => point.Y);
        return Math.Max(0, bottom - top);
    }
}

internal static class ImageCleanerWorkerProtocol
{
    private const int Magic = 0x4D494345;
    private const int Version = 1;
    private const int MaxRegionCount = 100_000;
    private const int MaxPolygonPointCount = 10_000;

    internal static void WriteRequest(BinaryWriter writer, ImageCleanerWorkerRequest request)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(request);
        writer.Write(Magic);
        writer.Write(Version);
        WindowsImageFrameProtocol.Write(writer, request.Source);
        writer.Write(request.Regions.Count);
        foreach (var region in request.Regions)
        {
            writer.Write(region.Polygon.Count);
            foreach (var point in region.Polygon)
            {
                writer.Write(point.X);
                writer.Write(point.Y);
            }
        }
        writer.Flush();
    }

    internal static ImageCleanerWorkerRequest ReadRequest(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        EnsureHeader(reader);
        var source = WindowsImageFrameProtocol.Read(reader);
        var regionCount = reader.ReadInt32();
        if (regionCount < 0 || regionCount > MaxRegionCount)
            throw new InvalidDataException("Image cleaner region count is invalid.");
        var regions = new OcrTextRegion[regionCount];
        for (var regionIndex = 0; regionIndex < regionCount; regionIndex++)
        {
            var pointCount = reader.ReadInt32();
            if (pointCount < 0 || pointCount > MaxPolygonPointCount)
                throw new InvalidDataException("Image cleaner polygon point count is invalid.");
            var points = new ImagePoint[pointCount];
            for (var pointIndex = 0; pointIndex < pointCount; pointIndex++)
                points[pointIndex] = new ImagePoint(reader.ReadDouble(), reader.ReadDouble());
            regions[regionIndex] = new OcrTextRegion(string.Empty, points, 0);
        }
        return new ImageCleanerWorkerRequest(source, regions);
    }

    internal static void WriteResponse(BinaryWriter writer, ImageCleanerWorkerResponse response)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(response);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write((byte)response.Status);
        if (response.Status == ImageCleanerWorkerStatus.Success)
            WindowsImageFrameProtocol.Write(writer, response.Image!);
        else
            writer.Write(response.ErrorMessage ?? string.Empty);
        writer.Flush();
    }

    internal static ImageCleanerWorkerResponse ReadResponse(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        EnsureHeader(reader);
        var status = (ImageCleanerWorkerStatus)reader.ReadByte();
        if (!Enum.IsDefined(status))
            throw new InvalidDataException("Image cleaner response status is invalid.");
        return status == ImageCleanerWorkerStatus.Success
            ? ImageCleanerWorkerResponse.Success(WindowsImageFrameProtocol.Read(reader))
            : ImageCleanerWorkerResponse.Failure(reader.ReadString());
    }

    private static void EnsureHeader(BinaryReader reader)
    {
        if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version)
            throw new InvalidDataException("Image cleaner worker protocol header is invalid.");
    }
}

internal sealed record ImageCleanerWorkerRequest(
    ImageFrame Source,
    IReadOnlyList<OcrTextRegion> Regions);

internal enum ImageCleanerWorkerStatus : byte
{
    Success = 0,
    Failed = 1
}

internal sealed record ImageCleanerWorkerResponse(
    ImageCleanerWorkerStatus Status,
    ImageFrame? Image,
    string? ErrorMessage)
{
    internal static ImageCleanerWorkerResponse Success(ImageFrame image) =>
        new(ImageCleanerWorkerStatus.Success, image, null);

    internal static ImageCleanerWorkerResponse Failure(string errorMessage) =>
        new(ImageCleanerWorkerStatus.Failed, null, errorMessage);
}
