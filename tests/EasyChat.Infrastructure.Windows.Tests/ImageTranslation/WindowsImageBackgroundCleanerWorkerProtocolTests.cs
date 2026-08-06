using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.Windows.ImageTranslation;

namespace EasyChat.Infrastructure.Windows.Tests.ImageTranslation;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsImageBackgroundCleanerWorkerProtocolTests
{
    [TestMethod]
    public void Request_RoundTripsFrameStrideDpiAndPolygons()
    {
        var expected = new ImageCleanerWorkerRequest(
            CreateFrame(),
            [
                new OcrTextRegion(
                    "ignored",
                    [new ImagePoint(1.5, 2.5), new ImagePoint(3.5, 4.5)],
                    12,
                    0.9)
            ]);

        var actual = RoundTrip(
            writer => ImageCleanerWorkerProtocol.WriteRequest(writer, expected),
            ImageCleanerWorkerProtocol.ReadRequest);

        AssertFrameEqual(expected.Source, actual.Source);
        Assert.HasCount(1, actual.Regions);
        Assert.AreEqual(expected.Regions[0].Polygon[1], actual.Regions[0].Polygon[1]);
    }

    [TestMethod]
    public void Response_RoundTripsImageAndFailure()
    {
        var success = RoundTrip(
            writer => ImageCleanerWorkerProtocol.WriteResponse(
                writer,
                ImageCleanerWorkerResponse.Success(CreateFrame())),
            ImageCleanerWorkerProtocol.ReadResponse);
        Assert.AreEqual(ImageCleanerWorkerStatus.Success, success.Status);
        Assert.IsNotNull(success.Image);
        AssertFrameEqual(CreateFrame(), success.Image);

        var failure = RoundTrip(
            writer => ImageCleanerWorkerProtocol.WriteResponse(
                writer,
                ImageCleanerWorkerResponse.Failure("failed")),
            ImageCleanerWorkerProtocol.ReadResponse);
        Assert.AreEqual(ImageCleanerWorkerStatus.Failed, failure.Status);
        Assert.AreEqual("failed", failure.ErrorMessage);
        Assert.IsNull(failure.Image);
    }

    [TestMethod]
    public async Task Worker_ProcessesOneRequestAndThenExits()
    {
        var pipeName = "EasyChat.ImageCleaner.Tests." + Guid.NewGuid().ToString("N");
        var worker = Task.Run(() => WindowsImageBackgroundCleanerWorker.Run(pipeName));
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await client.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
        using var reader = new BinaryReader(client, Encoding.UTF8, leaveOpen: true);
        using var writer = new BinaryWriter(client, Encoding.UTF8, leaveOpen: true);

        ImageCleanerWorkerProtocol.WriteRequest(
            writer,
            new ImageCleanerWorkerRequest(CreateFrame(), []));
        var response = await Task.Run(() => ImageCleanerWorkerProtocol.ReadResponse(reader))
            .WaitAsync(TimeSpan.FromSeconds(5));
        await worker.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(ImageCleanerWorkerStatus.Success, response.Status);
        Assert.IsNotNull(response.Image);
        AssertFrameEqual(CreateFrame(), response.Image);
        Assert.IsTrue(worker.IsCompletedSuccessfully);
    }

    private static ImageFrame CreateFrame() =>
        new(
            2,
            1,
            12,
            120,
            144,
            new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 });

    private static void AssertFrameEqual(ImageFrame expected, ImageFrame actual)
    {
        Assert.AreEqual(expected.Width, actual.Width);
        Assert.AreEqual(expected.Height, actual.Height);
        Assert.AreEqual(expected.Stride, actual.Stride);
        Assert.AreEqual(expected.DpiX, actual.DpiX);
        Assert.AreEqual(expected.DpiY, actual.DpiY);
        Assert.AreEqual(expected.PixelFormat, actual.PixelFormat);
        CollectionAssert.AreEqual(expected.Pixels.ToArray(), actual.Pixels.ToArray());
    }

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
