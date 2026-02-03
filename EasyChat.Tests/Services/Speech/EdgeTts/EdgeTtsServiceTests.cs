using EasyChat.Services.Abstractions;
using EasyChat.Services.Speech;
using EasyChat.Services.Speech.Tts;
using EasyChat.Services.Speech.Tts.EdgeTts;

namespace EasyChat.Tests.Services.Speech.EdgeTts;

[TestClass]
public class EdgeTtsServiceTests
{
    [TestMethod]
    public async Task TestGetSupportedLanguages_ReturnsMappedLanguages()
    {
        // Arrange
        EdgeTtsVoiceProvider provider = new EdgeTtsVoiceProvider();
        EdgeTtsService service = new EdgeTtsService(provider);
        await provider.InitializeAsync();

        // Act
        var languages = service.GetSupportedLanguages();

        // Assert
        Assert.IsNotNull(languages, "Languages list should not be null.");
        Assert.IsNotEmpty(languages, "Languages list should not be empty.");

        // Verify a known language exists (e.g., en-US)
        var enUs = languages.Find(l => l.Locale == "en-US");
        Assert.IsNotNull(enUs, "en-US language not found in supported languages.");
        Assert.AreEqual("English", enUs.Language);
        Assert.EndsWith(".png", enUs.Flag, "Flag property should be populated.");
        
        Console.WriteLine(@$"Found {languages.Count} supported languages.");
    }

    [TestMethod]
    public async Task TestGetVoices_ReturnsGenericVoiceDefinitions()
    {
        // Arrange
        EdgeTtsVoiceProvider provider = new EdgeTtsVoiceProvider();
        EdgeTtsService service = new EdgeTtsService(provider);
        await provider.InitializeAsync();

        // Act
        var voices = service.GetVoices();

        // Assert
        Assert.IsNotNull(voices, "Voices list should not be null.");
        Assert.IsNotEmpty(voices, "Voices list should not be empty.");

        // Verify mapping of a specific voice
        var voice = voices.Find(v => v.Id == "zu-ZA-ThembaNeural");
        Assert.IsNotNull(voice, "Voice 'zu-ZA-ThembaNeural' not found.");
        
        // Check generic properties
        Assert.AreEqual("Themba", voice.Name); // Role is mapped to Name
        Assert.AreEqual("zu-ZA", voice.LanguageId); // LanguageId derived
        Assert.AreEqual("Male", voice.Gender);
        
        Console.WriteLine(@$"Found {voices.Count} voices.");
        Console.WriteLine(@$"Example Voice: {voice.Name} ({voice.Id}) - {voice.LanguageId}");
    }

    [TestMethod]
    public async Task TestSynthesizeAsync_WithOptionalParameters()
    {
        // Arrange
        EdgeTtsVoiceProvider provider = new EdgeTtsVoiceProvider();
        EdgeTtsService service = new EdgeTtsService(provider);
        await provider.InitializeAsync();

        var voiceId = "en-US-AriaNeural";
        var text = "Hello, this is a test of the generic TTS interface.";
        var outputFile = "test_generic_synthesis.mp3";

        // Act
        Console.WriteLine(@$"Synthesizing '{text}' with {voiceId}...");
        // Test with optional parameters (speed up slightly)
        await service.SynthesizeAsync(text, voiceId, outputFile, rate: "+10%");

        // Assert
        Assert.IsTrue(File.Exists(outputFile), "Output file should have been created.");
        var fileInfo = new FileInfo(outputFile);
        Assert.IsGreaterThan(0, fileInfo.Length, "Output file should not be empty.");
        
        Console.WriteLine(@$"Synthesis complete. File size: {fileInfo.Length} bytes.");

        // Cleanup
        if (File.Exists(outputFile))
        {
            File.Delete(outputFile);
        }
    }

    [TestMethod]
    public async Task TestGetVoices_FilterByLanguage()
    {
        // Arrange
        EdgeTtsVoiceProvider provider = new EdgeTtsVoiceProvider();
        EdgeTtsService service = new EdgeTtsService(provider);
        await provider.InitializeAsync();

        // Act
        var allVoices = service.GetVoices();
        
        // Find "Chinese (China)" language definition
        var targetLang = TtsLanguageService.Languages.FirstOrDefault(l => l.Locale == TtsLanguageService.Locales.Zh_CN);
        Assert.IsNotNull(targetLang, "zh-CN should define in TtsLanguageService");

        // Filtering by LanguageId from the definition
        var chineseVoices = allVoices.Where(v => v.LanguageId == TtsLanguageService.Locales.Zh_CN).ToList();

        // Assert
        Assert.IsNotEmpty(chineseVoices, "Should find Chinese voices.");
        Console.WriteLine(@$"Found {chineseVoices.Count} Chinese voices.");
        foreach (var voice in chineseVoices)
        {
            Assert.AreEqual(targetLang.Locale, voice.LanguageId);
            Console.WriteLine(@$"Chinese Voice: {voice.Name} ({voice.Id})");
        }
    }
}
