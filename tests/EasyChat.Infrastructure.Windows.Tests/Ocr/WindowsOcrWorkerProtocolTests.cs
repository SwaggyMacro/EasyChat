using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.Windows.Ocr;

namespace EasyChat.Infrastructure.Windows.Tests.Ocr;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsOcrWorkerProtocolTests
{
    [TestMethod]
    public void Request_RoundTripsAllRecognitionInputs()
    {
        var expected = new OcrWorkerRequest(
            @"C:\models\ocr",
            "ja",
            true,
            new ImageFrame(
                2,
                1,
                12,
                120,
                144,
                new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }));

        var actual = RoundTrip(
            writer => OcrWorkerProtocol.WriteRequest(writer, expected),
            OcrWorkerProtocol.ReadRequest);

        Assert.AreEqual(expected.ModelDirectory, actual.ModelDirectory);
        Assert.AreEqual(expected.LanguageId, actual.LanguageId);
        Assert.AreEqual(expected.EnableRotation, actual.EnableRotation);
        Assert.AreEqual(expected.Image.Width, actual.Image.Width);
        Assert.AreEqual(expected.Image.Height, actual.Image.Height);
        Assert.AreEqual(expected.Image.Stride, actual.Image.Stride);
        Assert.AreEqual(expected.Image.DpiX, actual.Image.DpiX);
        Assert.AreEqual(expected.Image.DpiY, actual.Image.DpiY);
        CollectionAssert.AreEqual(
            expected.Image.Pixels.ToArray(),
            actual.Image.Pixels.ToArray());
    }

    [TestMethod]
    public void ImageFrameRequest_StreamsBgraPixelsIncludingStridePadding()
    {
        var image = new ImageFrame(
            1,
            2,
            8,
            96,
            96,
            new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 });

        var actual = RoundTrip(
            writer => OcrWorkerProtocol.WriteRequest(
                writer,
                new OcrWorkerRequest(@"C:\models\ocr", "en", false, image)),
            OcrWorkerProtocol.ReadRequest);

        Assert.AreEqual(1, actual.Image.Width);
        Assert.AreEqual(2, actual.Image.Height);
        Assert.AreEqual(8, actual.Image.Stride);
        CollectionAssert.AreEqual(
            image.Pixels.ToArray(),
            actual.Image.Pixels.ToArray());
    }

    [TestMethod]
    public void PersistentRequestReader_ReusesLargestImageBuffer()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            OcrWorkerProtocol.WriteRequest(
                writer,
                new OcrWorkerRequest(@"C:\models\ocr", "en", false, CreatePixel()));
            OcrWorkerProtocol.WriteRequest(
                writer,
                new OcrWorkerRequest(@"C:\models\ocr", "en", false, CreatePixel()));
        }
        stream.Position = 0;
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        byte[]? reusableBuffer = null;

        var first = OcrWorkerProtocol.ReadRequest(reader, ref reusableBuffer);
        Assert.IsTrue(MemoryMarshal.TryGetArray(first.Image.Pixels, out var firstBuffer));
        var second = OcrWorkerProtocol.ReadRequest(reader, ref reusableBuffer);
        Assert.IsTrue(MemoryMarshal.TryGetArray(second.Image.Pixels, out var secondBuffer));

        Assert.AreSame(firstBuffer.Array, secondBuffer.Array);
        Assert.AreSame(reusableBuffer, secondBuffer.Array);
    }

    [TestMethod]
    public void Response_RoundTripsTextGeometryAndErrors()
    {
        var success = OcrWorkerResponse.Success(
        [
            new WindowsOcrBackendRegion(
                "text",
                [new WindowsOcrPoint(1.5, 2.5), new WindowsOcrPoint(3.5, 4.5)],
                12.5,
                0.91)
        ]);

        var successResult = RoundTrip(
            writer => OcrWorkerProtocol.WriteResponse(writer, success),
            OcrWorkerProtocol.ReadResponse);
        var region = successResult.Regions.Single();
        Assert.AreEqual(OcrWorkerStatus.Success, successResult.Status);
        Assert.AreEqual("text", region.Text);
        Assert.AreEqual(12.5, region.FallbackAngle);
        Assert.AreEqual(0.91, region.Confidence);
        Assert.AreEqual(new WindowsOcrPoint(3.5, 4.5), region.Polygon[1]);

        var failure = OcrWorkerResponse.Failure(OcrWorkerStatus.Unsupported, "unsupported");
        var failureResult = RoundTrip(
            writer => OcrWorkerProtocol.WriteResponse(writer, failure),
            OcrWorkerProtocol.ReadResponse);
        Assert.AreEqual(OcrWorkerStatus.Unsupported, failureResult.Status);
        Assert.AreEqual("unsupported", failureResult.ErrorMessage);
        Assert.IsEmpty(failureResult.Regions);
    }

    [TestMethod]
    public async Task Worker_ProcessesOnePipeRequestAndThenExits()
    {
        var pipeName = "EasyChat.Ocr.Tests." + Guid.NewGuid().ToString("N");
        var worker = Task.Run(() => WindowsOcrWorker.Run(pipeName));
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await client.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var reader = new BinaryReader(client, Encoding.UTF8, leaveOpen: true);
        using var writer = new BinaryWriter(client, Encoding.UTF8, leaveOpen: true);

        OcrWorkerProtocol.WriteRequest(
            writer,
            new OcrWorkerRequest(
                @"C:\models\ocr",
                "unsupported-language",
                false,
                CreatePixel()));
        var response = await Task.Run(() => OcrWorkerProtocol.ReadResponse(reader))
            .WaitAsync(TimeSpan.FromSeconds(5));
        await worker.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(OcrWorkerStatus.Unsupported, response.Status);
        StringAssert.Contains(response.ErrorMessage, "unsupported-language");
        Assert.IsTrue(worker.IsCompletedSuccessfully);
    }

    [TestMethod]
    public async Task PersistentWorker_ProcessesMultipleRequestsUntilClientDisconnects()
    {
        var pipeName = "EasyChat.Ocr.Tests." + Guid.NewGuid().ToString("N");
        var worker = Task.Run(() => WindowsOcrWorker.Run(pipeName, persistent: true));
        var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await client.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var reader = new BinaryReader(client, Encoding.UTF8, leaveOpen: true);
        var writer = new BinaryWriter(client, Encoding.UTF8, leaveOpen: true);

        for (var index = 0; index < 2; index++)
        {
            OcrWorkerProtocol.WriteRequest(
                writer,
                new OcrWorkerRequest(
                    @"C:\models\ocr",
                    "unsupported-language",
                    false,
                    CreatePixel()));
            var response = await Task.Run(() => OcrWorkerProtocol.ReadResponse(reader))
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(OcrWorkerStatus.Unsupported, response.Status);
            Assert.IsFalse(worker.IsCompleted);
        }

        writer.Dispose();
        reader.Dispose();
        await client.DisposeAsync();
        await worker.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(worker.IsCompletedSuccessfully);
    }

    private static ImageFrame CreatePixel() =>
        new(1, 1, 4, 96, 96, new byte[] { 0, 0, 0, 255 });

    private static T RoundTrip<T>(Action<BinaryWriter> write, Func<BinaryReader, T> read)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            write(writer);
        stream.Position = 0;
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        return read(reader);
    }
}
