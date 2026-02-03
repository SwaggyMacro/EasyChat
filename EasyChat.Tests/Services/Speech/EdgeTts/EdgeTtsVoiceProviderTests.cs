using EasyChat.Services.Abstractions;
using EasyChat.Services.Speech;
using EasyChat.Services.Speech.Tts.EdgeTts;

namespace EasyChat.Tests.Services.Speech.EdgeTts;

[TestClass]
public class EdgeTtsVoiceProviderTests
{
    [TestMethod]
    public async Task TestInitializeAsync_LoadsVoicesFromJson()
    {
        // Arrange
        EdgeTtsVoiceProvider provider = new EdgeTtsVoiceProvider();

        // Act
        await provider.InitializeAsync();

        // Assert
        Assert.IsNotNull(provider.Voices, "Voices list should not be null.");
        Assert.IsNotEmpty(provider.Voices, "Voices list should not be empty.");

        // Verification of a specific voice (e.g., ThembaNeural as modified recently)
        var themba = provider.Voices.Find(v => v.Name == "zu-ZA-ThembaNeural");
        Assert.IsNotNull(themba, "Voice 'zu-ZA-ThembaNeural' not found.");
        Assert.AreEqual("Male", themba.Gender);
        Assert.AreEqual("General", themba.ContentCategories);
        Assert.AreEqual("Themba", themba.Role);
        Assert.AreEqual("Zulu (SouthAfrica)", themba.EnglishName);
        Assert.AreEqual("zu", themba.Language);
        Assert.AreEqual("ZA", themba.Region);

        Console.WriteLine(@$"Successfully loaded {provider.Voices.Count} voices.");
        foreach (var voice in provider.Voices) // Print first 5 for visual verification
        {
            if (provider.Voices.IndexOf(voice) < 5)
                Console.WriteLine(@$"- {voice.EnglishName}: {voice.Name} (Role: {voice.Role})");
        }
    }

    [TestMethod]
    public async Task TestSynthesizeWithProviderVoice()
    {
        // Arrange
        EdgeTtsVoiceProvider provider = new EdgeTtsVoiceProvider();
        ITtsService client = new EdgeTtsService(provider);
        await provider.InitializeAsync();

        var voiceDef = provider.Voices.Find(v => v.Name == "zu-ZA-ThembaNeural");
        Assert.IsNotNull(voiceDef, "Voice not found");

        string text = "Sawubona, nina nonke! Lolu uhlelo lokuhlola."; // "Hello everyone! This is a test." in Zulu
        string outputFile = "test_synthesis_themba.mp3";

        // Act
        Console.WriteLine(@$"Synthesizing with {voiceDef.Name} ({voiceDef.Role})...");
        await client.SynthesizeAsync(text, voiceDef.Name, outputFile);

        // Assert
        Assert.IsTrue(File.Exists(outputFile), "Output file should exist");
        var info = new FileInfo(outputFile);
        Assert.IsGreaterThan(0, info.Length, "Output file should not be empty");
        Console.WriteLine(@$"Generated: {info.FullName} ({info.Length} bytes)");
        
        // Cleanup
        if (File.Exists(outputFile)) File.Delete(outputFile);
    }
}
