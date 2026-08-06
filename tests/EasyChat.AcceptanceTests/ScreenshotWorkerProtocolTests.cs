using EasyChat.Contracts.Capture;
using EasyChat.Contracts.Platform;
using EasyChat.Desktop.Windows.Capture;

namespace EasyChat.AcceptanceTests;

[TestClass]
public sealed class ScreenshotWorkerProtocolTests
{
    [TestMethod]
    public void ReadyResponseRoundTrips()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            ScreenshotWorkerProtocol.WriteReady(writer);
        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        ScreenshotWorkerProtocol.ReadReady(reader);
    }

    [TestMethod]
    public void RequestRoundTripsCaptureOptions()
    {
        var request = new ScreenshotWorkerRequest(
            true,
            "Dark",
            "zh-CN",
            CaptureOverlayAction.OcrWorkbench,
            CaptureToolbarMode.ImageSelection);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            ScreenshotWorkerProtocol.WriteRequest(writer, request);
        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        var actual = ScreenshotWorkerProtocol.ReadRequest(reader);

        Assert.AreEqual(request, actual);
    }

    [TestMethod]
    public void RequestRejectsInvalidToolbarMode()
    {
        var request = new ScreenshotWorkerRequest(
            true,
            "Dark",
            "zh-CN",
            CaptureOverlayAction.OcrWorkbench,
            CaptureToolbarMode.Full);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            ScreenshotWorkerProtocol.WriteRequest(writer, request);
        var bytes = stream.ToArray();
        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, bytes.Length - sizeof(int));
        using var invalidStream = new MemoryStream(bytes);
        using var reader = new BinaryReader(invalidStream);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            ScreenshotWorkerProtocol.ReadRequest(reader));
    }

    [TestMethod]
    public void SuccessResponseRoundTripsSelection()
    {
        var pixels = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();
        var selection = new ScreenshotSelection(
            new ImageFrame(2, 2, 8, 96, 96, pixels),
            CaptureOverlayAction.CopyBilingual,
            new PhysicalScreenPoint(-12, 34));
        using var stream = new MemoryStream();

        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            ScreenshotWorkerProtocol.WriteSuccess(writer, selection);
        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        var actual = ScreenshotWorkerProtocol.Read(reader);

        Assert.IsNotNull(actual);
        Assert.AreEqual(selection.Action, actual.Action);
        Assert.AreEqual(selection.CompletionPoint, actual.CompletionPoint);
        Assert.AreEqual(2, actual.Image.Width);
        Assert.AreEqual(2, actual.Image.Height);
        CollectionAssert.AreEqual(pixels, actual.Image.Pixels.ToArray());
    }

    [TestMethod]
    public void CancelledResponseReturnsNull()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            ScreenshotWorkerProtocol.WriteCancelled(writer);
        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        Assert.IsNull(ScreenshotWorkerProtocol.Read(reader));
    }

    [TestMethod]
    public void FailureResponsePreservesMessage()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            ScreenshotWorkerProtocol.WriteFailure(writer, new InvalidOperationException("capture failed"));
        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            ScreenshotWorkerProtocol.Read(reader));

        StringAssert.Contains(exception.Message, "capture failed");
    }
}
