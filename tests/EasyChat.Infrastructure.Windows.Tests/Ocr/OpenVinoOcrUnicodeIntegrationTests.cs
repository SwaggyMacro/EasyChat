using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using EasyChat.Contracts.ApplicationData;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.Windows.Ocr;
using OpenCvSharp;

namespace EasyChat.Infrastructure.Windows.Tests.Ocr;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class OpenVinoOcrUnicodeIntegrationTests
{
    [TestMethod]
    [TestCategory("Integration")]
    public async Task V6Small_DownloadsAndRecognizesFromUnicodeModelDirectory()
    {
        if (Environment.GetEnvironmentVariable("EASYCHAT_RUN_OCR_INTEGRATION") != "1")
            Assert.Inconclusive("Set EASYCHAT_RUN_OCR_INTEGRATION=1 to run the real model smoke test.");

        var root = Path.Combine(
            Path.GetTempPath(),
            $"EasyChat-OCR-\u6A21\u578B-\U0001F680-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var paths = new FixedApplicationDataPaths(root);
            using var ocr = new WindowsOpenVinoOcr(paths);
            var package = ocr.ModelPackages.Single(candidate =>
                candidate.Id == OpenVinoOcrModelCatalog.UniversalV6SmallId);
            await ocr.DownloadModelAsync(package, new OcrModelDownloadOptions(null, false));

            var result = await ocr.RecognizeAsync(new OcrRecognitionRequest(
                CreateTextFrame("OpenVINO 123"),
                OcrLanguages.English,
                EnableRotation: true));

            Assert.IsTrue(result.Text.Contains("OpenVINO", StringComparison.OrdinalIgnoreCase), result.Text);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task V4AndV3PaddleModels_DownloadAndRecognizeWithOpenVino()
    {
        if (Environment.GetEnvironmentVariable("EASYCHAT_RUN_OCR_INTEGRATION") != "1")
            Assert.Inconclusive("Set EASYCHAT_RUN_OCR_INTEGRATION=1 to run the real model smoke test.");

        var root = Path.Combine(
            Path.GetTempPath(),
            $"EasyChat-OCR-Paddle-\u6A21\u578B-\U0001F680-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var paths = new FixedApplicationDataPaths(root);
            using var ocr = new WindowsOpenVinoOcr(paths);
            var cases = new[]
            {
                (PackageId: OpenVinoOcrModelCatalog.KoreanV4Id, Language: OcrLanguages.Korean),
                (PackageId: OpenVinoOcrModelCatalog.KannadaV4Id, Language: OcrLanguages.Kannada),
                (PackageId: OpenVinoOcrModelCatalog.CyrillicV3Id, Language: GetLanguage("ru"))
            };

            foreach (var item in cases)
            {
                var package = ocr.ModelPackages.Single(candidate => candidate.Id == item.PackageId);
                await ocr.DownloadModelAsync(package, new OcrModelDownloadOptions(null, false));
                var result = await ocr.RecognizeAsync(new OcrRecognitionRequest(
                    CreateTextFrame("123456"),
                    item.Language,
                    EnableRotation: true));

                Assert.IsFalse(string.IsNullOrWhiteSpace(result.Text), item.PackageId);
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static ImageFrame CreateTextFrame(string text)
    {
        using var bgr = new Mat(new Size(640, 160), MatType.CV_8UC3, Scalar.White);
        Cv2.PutText(
            bgr,
            text,
            new Point(24, 105),
            HersheyFonts.HersheySimplex,
            2.2,
            Scalar.Black,
            4,
            LineTypes.AntiAlias);
        using var bgra = new Mat();
        Cv2.CvtColor(bgr, bgra, ColorConversionCodes.BGR2BGRA);
        var pixels = new byte[checked((int)(bgra.Total() * bgra.ElemSize()))];
        Marshal.Copy(bgra.Data, pixels, 0, pixels.Length);
        return new ImageFrame(bgra.Width, bgra.Height, checked((int)bgra.Step()), 96, 96, pixels);
    }

    private static OcrLanguage GetLanguage(string id) =>
        OcrLanguages.TryGet(id, out var language)
            ? language
            : throw new InvalidOperationException($"Missing OCR language '{id}'.");

    private sealed class FixedApplicationDataPaths(string root) : IApplicationDataPaths
    {
        public event EventHandler<ApplicationDataLocationChangedEventArgs>? LocationChanged
        {
            add { }
            remove { }
        }

        public ApplicationDataLocation Current { get; } = new(root, IsDefault: false);
        public string ConfigurationDirectory { get; } = Path.Combine(root, "Configuration");
        public string SpeechModelsDirectory { get; } = Path.Combine(root, "Models", "ASR");
        public string OcrModelsDirectory { get; } = Path.Combine(root, "Models", "OCR");
    }
}
