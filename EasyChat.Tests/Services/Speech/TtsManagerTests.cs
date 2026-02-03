using EasyChat.Services.Abstractions;
using EasyChat.Services.Speech;
using EasyChat.Services.Speech.Tts;
using EasyChat.Services.Speech.Tts.EdgeTts;

namespace EasyChat.Tests.Services.Speech;

[TestClass]
public class TtsManagerTests
{
    // Subclass to simulate a second provider with a different ID
    // We strictly implement ITtsService to ensure the interface call uses our new ProviderId
    private class SecondEdgeTtsService : EdgeTtsService, ITtsService
    {
        public SecondEdgeTtsService(EdgeTtsVoiceProvider provider) : base(provider) { }
        public new string ProviderId => "EdgeTTS-Backup";
    }

    private EdgeTtsService _edgeService = default!;
    private SecondEdgeTtsService _secondService = default!;
    private TtsManager _manager = default!;
    private EdgeTtsVoiceProvider _commonProvider = default!;

    [TestInitialize]
    public async Task Setup()
    {
        _commonProvider = new EdgeTtsVoiceProvider();
        await _commonProvider.InitializeAsync(); // Required for service to work

        _edgeService = new EdgeTtsService(_commonProvider);
        _secondService = new SecondEdgeTtsService(_commonProvider);

        var providers = new List<ITtsService> { _edgeService, _secondService };
        _manager = new TtsManager(providers);
    }
    
    [TestMethod]
    public async Task TestSynthesizeAsync_Success()
    {
        // ... implementation ...
        // Arrange
        var text = "Hello from TTS Manager Manager Test";
        var voiceId = "en-US-AriaNeural"; // Ensure this voice exists in your provider
        var outputFile = "manager_test_output.mp3";

        try
        {
            // Act
            // This will use the default provider (EdgeTTS)
            await _manager.SynthesizeAsync(text, voiceId, outputFile);

            // Assert
            Assert.IsTrue(File.Exists(outputFile), "Output file should differ");
            var fileInfo = new FileInfo(outputFile);
            Assert.IsGreaterThan(0, fileInfo.Length, "File should not be empty");
        }
        finally
        {
            if (File.Exists(outputFile))
            {
                // File.Delete(outputFile);
            }
        }
    }

    [TestMethod]
    public void TestDefaultProvider_IsEdgeTTS()
    {
        Assert.AreEqual(TtsProviders.EdgeTTS, _manager.ProviderId);
    }

    [TestMethod]
    public void TestSwitchProvider_ChangesCurrentProvider()
    {
        // Act
        _manager.SwitchProvider("EdgeTTS-Backup");

        // Assert
        Assert.AreEqual("EdgeTTS-Backup", _manager.ProviderId);
    }

    [TestMethod]
    public void TestGetAvailableProviders_ReturnsAllRegisteredIds()
    {
        // Act
        var providers = _manager.GetAvailableProviders();

        // Assert
        Assert.HasCount(2, providers);
        CollectionAssert.Contains(providers, TtsProviders.EdgeTTS);
        CollectionAssert.Contains(providers, "EdgeTTS-Backup");
    }

    [TestMethod]
    public void TestGetVoices_DelegatesToCorrectProvider()
    {
        // Act
        var voices1 = _manager.GetVoices();
        Assert.IsNotEmpty(voices1);
        
        // Switch
        _manager.SwitchProvider("EdgeTTS-Backup");
        var voices2 = _manager.GetVoices();
        
        // Assert
        Assert.HasCount(voices1.Count, voices2); // Same backing provider
    }
}

