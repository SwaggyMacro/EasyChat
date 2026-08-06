using System.Runtime.Versioning;
using EasyChat.Contracts.ApplicationData;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Platform;
using EasyChat.Infrastructure.Windows.Ocr;

namespace EasyChat.Infrastructure.Windows.Tests.Ocr;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class OpenVinoOcrIdleReleaseTests
{
    [TestMethod]
    public void IdleRelease_ResetsTimerAndIgnoresCanceledCallbacks()
    {
        var paths = new FixedApplicationDataPaths(Path.GetTempPath());
        var time = new ControlledTimeProvider();
        var workers = new List<FakeWorker>();
        using var backend = new OpenVinoWindowsOcrBackend(
            paths,
            logger: null,
            _ =>
            {
                var worker = new FakeWorker();
                workers.Add(worker);
                return worker;
            },
            time);
        var language = OpenVinoOcrModelCatalog.ResolveLanguage(OcrLanguages.English);
        var frame = new ImageFrame(1, 1, 4, 96, 96, new byte[] { 0, 0, 0, 255 });

        backend.Recognize(
            frame, language, false, OcrRecognitionMode.IdleRelease, 45, CancellationToken.None);
        var firstTimer = time.LatestTimer;
        backend.Recognize(
            frame, language, false, OcrRecognitionMode.IdleRelease, 60, CancellationToken.None);
        var secondTimer = time.LatestTimer;

        Assert.HasCount(1, workers);
        Assert.IsTrue(firstTimer.IsDisposed);
        Assert.AreEqual(TimeSpan.FromSeconds(60), secondTimer.DueTime);

        firstTimer.Fire();
        Assert.IsFalse(workers[0].IsDisposed);

        secondTimer.Fire();
        Assert.IsTrue(workers[0].IsDisposed);
    }

    [TestMethod]
    public void FastMode_CancelsIdleReleaseTimerWithoutClosingWorker()
    {
        var paths = new FixedApplicationDataPaths(Path.GetTempPath());
        var time = new ControlledTimeProvider();
        var worker = new FakeWorker();
        using var backend = new OpenVinoWindowsOcrBackend(
            paths,
            logger: null,
            _ => worker,
            time);
        var language = OpenVinoOcrModelCatalog.ResolveLanguage(OcrLanguages.English);
        var frame = new ImageFrame(1, 1, 4, 96, 96, new byte[] { 0, 0, 0, 255 });

        backend.Recognize(
            frame, language, false, OcrRecognitionMode.IdleRelease, 30, CancellationToken.None);
        var canceledTimer = time.LatestTimer;
        backend.Recognize(
            frame, language, false, OcrRecognitionMode.Fast, 30, CancellationToken.None);

        Assert.IsTrue(canceledTimer.IsDisposed);
        canceledTimer.Fire();
        Assert.IsFalse(worker.IsDisposed);
    }

    private sealed class FakeWorker : IWindowsOcrWorkerClient
    {
        public bool IsDisposed { get; private set; }

        public IReadOnlyList<WindowsOcrBackendRegion> Recognize(
            ImageFrame image,
            WindowsOcrLanguageSelection language,
            string modelDirectory,
            bool enableRotation,
            CancellationToken cancellationToken) => [];

        public void Dispose() => IsDisposed = true;
    }

    private sealed class ControlledTimeProvider : TimeProvider
    {
        public ControlledTimer LatestTimer { get; private set; } = null!;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            LatestTimer = new ControlledTimer(callback, state, dueTime);
            return LatestTimer;
        }
    }

    private sealed class ControlledTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime) : ITimer
    {
        public TimeSpan DueTime { get; private set; } = dueTime;
        public bool IsDisposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            DueTime = dueTime;
            return !IsDisposed;
        }

        public void Fire() => callback(state);
        public void Dispose() => IsDisposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedApplicationDataPaths(string root) : IApplicationDataPaths
    {
        public event EventHandler<ApplicationDataLocationChangedEventArgs>? LocationChanged
        {
            add { }
            remove { }
        }

        public ApplicationDataLocation Current { get; } = new(root, true);
        public string ConfigurationDirectory => Path.Combine(root, "config");
        public string SpeechModelsDirectory => Path.Combine(root, "speech");
        public string OcrModelsDirectory => Path.Combine(root, "ocr");
    }
}
