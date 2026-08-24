using System.Globalization;
using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Settings.Persistence;
using EasyChat.Infrastructure.Settings.Persistence;
using Newtonsoft.Json;

namespace EasyChat.Infrastructure.Tests.Settings;

[TestClass]
public sealed class SettingsPersistenceContractTests
{
    private static readonly string[] ExpectedFileNames =
    [
        "AiModel.json",
        "General.json",
        "Input.json",
        "MachineTrans.json",
        "Ocr.json",
        "Prompts.json",
        "Proxy.json",
        "Result.json",
        "Screenshot.json",
        "SelectionTranslation.json",
        "Shortcut.json",
        "SpeechRecognition.json",
        "TextAssist.json",
        "Tts.json"
    ];

    [TestMethod]
    public async Task ReadAllAsync_CreatesTheFourteenCompatibleDefaultFiles()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var gateway = new JsonSettingsPersistenceGateway(directory);

            var result = await gateway.ReadAllAsync();

            Assert.IsTrue(result.IsSuccess, result.Error.Message);
            CollectionAssert.AreEqual(
                ExpectedFileNames,
                Directory.GetFiles(directory)
                    .Select(Path.GetFileName)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
            Assert.AreEqual("auto", result.Value.General.SourceLanguage.Id);
            Assert.AreEqual("zh-Hans", result.Value.General.TargetLanguage.Id);
            Assert.AreEqual("AiModel", result.Value.General.TranslationEngine);
            Assert.AreEqual("OpenAI", result.Value.General.AiModel);
            Assert.AreEqual(ThemeMode.System, result.Value.General.BaseTheme);
            Assert.AreEqual(5000, result.Value.Result.AutoCloseDelay);
            Assert.AreEqual(InputDeliveryMode.Paste, result.Value.Input.DeliveryMode);
            Assert.AreEqual(OcrRecognitionMode.Normal, result.Value.Screenshot.OcrMode);
            Assert.AreEqual(
                ScreenshotSettings.DefaultOcrIdleTimeoutSeconds,
                result.Value.Screenshot.OcrIdleTimeoutSeconds);
            Assert.IsFalse(result.Value.Screenshot.ClosePreviousOcrWindow);
            Assert.AreEqual("EdgeTTS", result.Value.Tts.Provider);
            var expectedPrompts = ReadBuiltInPrompts(result.Value.General.DisplayLanguage);
            AssertPromptAssetWasImported(expectedPrompts, result.Value.Prompts);
            StringAssert.Contains(
                await File.ReadAllTextAsync(Path.Combine(directory, "General.json")),
                "\"BaseTheme\": \"Default\"");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task ScreenshotOcrSettings_RoundTripAndOldFilesUseCompatibleDefaults()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var gateway = new JsonSettingsPersistenceGateway(directory);
            var initial = await gateway.ReadAllAsync();
            Assert.IsTrue(initial.IsSuccess, initial.Error.Message);
            var changed = initial.Value with
            {
                Screenshot = initial.Value.Screenshot with
                {
                    OcrMode = OcrRecognitionMode.IdleRelease,
                    OcrIdleTimeoutSeconds = 45,
                    ClosePreviousOcrWindow = true
                }
            };

            var write = await gateway.WriteAsync(SettingsSection.Screenshot, changed);
            var reread = await gateway.ReadAllAsync();

            Assert.IsTrue(write.IsSuccess, write.Error.Message);
            Assert.IsTrue(reread.IsSuccess, reread.Error.Message);
            Assert.AreEqual(OcrRecognitionMode.IdleRelease, reread.Value.Screenshot.OcrMode);
            Assert.AreEqual(45, reread.Value.Screenshot.OcrIdleTimeoutSeconds);
            Assert.IsTrue(reread.Value.Screenshot.ClosePreviousOcrWindow);
            StringAssert.Contains(
                await File.ReadAllTextAsync(Path.Combine(directory, "Screenshot.json")),
                "\"ClosePreviousOcrWindow\": true");

            await File.WriteAllTextAsync(
                Path.Combine(directory, "Screenshot.json"),
                """
                {
                  "Mode": "Precise",
                  "FixedAreas": []
                }
                """);
            var previous = await gateway.ReadAllAsync();

            Assert.IsTrue(previous.IsSuccess, previous.Error.Message);
            Assert.AreEqual(OcrRecognitionMode.Normal, previous.Value.Screenshot.OcrMode);
            Assert.AreEqual(
                ScreenshotSettings.DefaultOcrIdleTimeoutSeconds,
                previous.Value.Screenshot.OcrIdleTimeoutSeconds);
            Assert.IsFalse(previous.Value.Screenshot.ClosePreviousOcrWindow);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task ReadAllAsync_AcceptsThePreviousLanguageFieldAlias()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var gateway = new JsonSettingsPersistenceGateway(directory);
            Assert.IsTrue((await gateway.ReadAllAsync()).IsSuccess);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "General.json"),
                """
                {
                  "Language": "English",
                  "TransEngine": null,
                  "UsingAiModel": null,
                  "BaseTheme": null
                }
                """);

            var result = await gateway.ReadAllAsync();

            Assert.IsTrue(result.IsSuccess, result.Error.Message);
            Assert.AreEqual("English", result.Value.General.DisplayLanguage);
            Assert.AreEqual("AiModel", result.Value.General.TranslationEngine);
            Assert.AreEqual("OpenAI", result.Value.General.AiModel);
            Assert.AreEqual(ThemeMode.System, result.Value.General.BaseTheme);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task ReadAllAsync_PreservesPreviousExplicitThemeChoices()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var gateway = new JsonSettingsPersistenceGateway(directory);
            Assert.IsTrue((await gateway.ReadAllAsync()).IsSuccess);

            await File.WriteAllTextAsync(
                Path.Combine(directory, "General.json"),
                """
                {
                  "BaseTheme": "Light"
                }
                """);
            var light = await gateway.ReadAllAsync();
            Assert.IsTrue(light.IsSuccess, light.Error.Message);
            Assert.AreEqual(ThemeMode.Light, light.Value.General.BaseTheme);

            await File.WriteAllTextAsync(
                Path.Combine(directory, "General.json"),
                """
                {
                  "BaseTheme": "Dark"
                }
                """);
            var dark = await gateway.ReadAllAsync();
            Assert.IsTrue(dark.IsSuccess, dark.Error.Message);
            Assert.AreEqual(ThemeMode.Dark, dark.Value.General.BaseTheme);

            var useSystem = dark.Value with
            {
                General = dark.Value.General with { BaseTheme = ThemeMode.System }
            };
            var write = await gateway.WriteAsync(SettingsSection.General, useSystem);
            Assert.IsTrue(write.IsSuccess, write.Error.Message);
            StringAssert.Contains(
                await File.ReadAllTextAsync(Path.Combine(directory, "General.json")),
                "\"BaseTheme\": \"Default\"");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task InputTranslationMode_RoundTripsAndOldFilesDefaultToNormalWindow()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var gateway = new JsonSettingsPersistenceGateway(directory);
            var initial = await gateway.ReadAllAsync();
            Assert.IsTrue(initial.IsSuccess, initial.Error.Message);
            Assert.AreEqual(InputTranslationMode.NormalWindow, initial.Value.Input.TranslationMode);

            var changed = initial.Value with
            {
                Input = initial.Value.Input with { TranslationMode = InputTranslationMode.Tsf }
            };
            var write = await gateway.WriteAsync(SettingsSection.Input, changed);
            var reread = await gateway.ReadAllAsync();

            Assert.IsTrue(write.IsSuccess, write.Error.Message);
            Assert.IsTrue(reread.IsSuccess, reread.Error.Message);
            Assert.AreEqual(InputTranslationMode.Tsf, reread.Value.Input.TranslationMode);
            StringAssert.Contains(
                await File.ReadAllTextAsync(Path.Combine(directory, "Input.json")),
                "\"TranslationMode\": 1");

            await File.WriteAllTextAsync(
                Path.Combine(directory, "Input.json"),
                "{ \"DeliveryMode\": \"Paste\" }");
            var previous = await gateway.ReadAllAsync();

            Assert.IsTrue(previous.IsSuccess, previous.Error.Message);
            Assert.AreEqual(InputTranslationMode.NormalWindow, previous.Value.Input.TranslationMode);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task CustomColorThemes_RoundTripAndMigrateTheLegacyActiveTheme()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var gateway = new JsonSettingsPersistenceGateway(directory);
            var initial = await gateway.ReadAllAsync();
            Assert.IsTrue(initial.IsSuccess, initial.Error.Message);

            var changed = initial.Value with
            {
                General = initial.Value.General with
                {
                    ColorTheme = "custom:ocean",
                    CustomColorThemes =
                    [
                        new CustomColorThemeSettings(
                            "custom:ocean",
                            "Ocean",
                            "#FF0EA5E9",
                            "#FF38BDF8"),
                        new CustomColorThemeSettings(
                            "custom:forest",
                            "Forest",
                            "#FF15803D",
                            "#FF4ADE80")
                    ]
                }
            };

            var write = await gateway.WriteAsync(SettingsSection.General, changed);
            var reread = await gateway.ReadAllAsync();

            Assert.IsTrue(write.IsSuccess, write.Error.Message);
            Assert.IsTrue(reread.IsSuccess, reread.Error.Message);
            Assert.AreEqual("custom:ocean", reread.Value.General.ColorTheme);
            Assert.HasCount(2, reread.Value.General.CustomColorThemes);
            CollectionAssert.AreEqual(
                new[] { "custom:ocean", "custom:forest" },
                reread.Value.General.CustomColorThemes.Select(theme => theme.Id).ToArray());
            StringAssert.Contains(
                await File.ReadAllTextAsync(Path.Combine(directory, "General.json")),
                "\"CustomColorThemes\"");

            await File.WriteAllTextAsync(
                Path.Combine(directory, "General.json"),
                """
                {
                  "ColorTheme": "Legacy Pink",
                  "CustomThemePrimaryColor": "#FFFF1493",
                  "CustomThemeAccentColor": "#FFFFC0CB"
                }
                """);

            var legacy = await gateway.ReadAllAsync();

            Assert.IsTrue(legacy.IsSuccess, legacy.Error.Message);
            Assert.HasCount(1, legacy.Value.General.CustomColorThemes);
            Assert.AreEqual("Legacy Pink", legacy.Value.General.CustomColorThemes[0].DisplayName);
            Assert.AreEqual("#FFFF1493", legacy.Value.General.CustomColorThemes[0].PrimaryColor);
            StringAssert.StartsWith(legacy.Value.General.ColorTheme, "legacy-custom:");

            var migrate = await gateway.WriteAsync(SettingsSection.General, legacy.Value);

            Assert.IsTrue(migrate.IsSuccess, migrate.Error.Message);
            StringAssert.Contains(
                await File.ReadAllTextAsync(Path.Combine(directory, "General.json")),
                "\"CustomColorThemes\"");
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public void GeneralDefaults_FollowSystemUiLanguageWithoutPersistingAnImplicitChoice()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            var english = new GeneralSettingsDto();
            Assert.AreEqual("English", english.DisplayLanguage);
            Assert.IsFalse(JsonConvert.SerializeObject(english).Contains(
                "DisplayLanguage",
                StringComparison.Ordinal));

            CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");
            var chinese = new GeneralSettingsDto();
            Assert.AreEqual("Simplified Chinese", chinese.DisplayLanguage);
            Assert.IsFalse(JsonConvert.SerializeObject(chinese).Contains(
                "DisplayLanguage",
                StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [TestMethod]
    public async Task ShortcutRemarks_RoundTripAndRemainCompatibleWithPreviousFiles()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var gateway = new JsonSettingsPersistenceGateway(directory);
            var initial = await gateway.ReadAllAsync();
            Assert.IsTrue(initial.IsSuccess, initial.Error.Message);
            var changed = initial.Value with
            {
                Shortcut = new ShortcutSettings(
                [
                    new ShortcutEntrySettings(
                        "InputTranslate",
                        null,
                        "Ctrl + Enter",
                        true,
                        "Translate and send")
                ])
            };

            var write = await gateway.WriteAsync(SettingsSection.Shortcut, changed);
            var reread = await gateway.ReadAllAsync();

            Assert.IsTrue(write.IsSuccess, write.Error.Message);
            Assert.IsTrue(reread.IsSuccess, reread.Error.Message);
            Assert.AreEqual("Translate and send", reread.Value.Shortcut.Entries.Single().Remark);
            StringAssert.Contains(
                await File.ReadAllTextAsync(Path.Combine(directory, "Shortcut.json")),
                "\"Remark\": \"Translate and send\"");

            await File.WriteAllTextAsync(
                Path.Combine(directory, "Shortcut.json"),
                """
                {
                  "Entries": [
                    {
                      "ActionType": "Screenshot",
                      "Parameter": null,
                      "KeyCombination": "Ctrl + F8",
                      "IsEnabled": true
                    }
                  ]
                }
                """);

            var previous = await gateway.ReadAllAsync();

            Assert.IsTrue(previous.IsSuccess, previous.Error.Message);
            Assert.IsNull(previous.Value.Shortcut.Entries.Single().Remark);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task SpeechRecognitionPromptId_RoundTripsAndOldFilesRemainCompatible()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var gateway = new JsonSettingsPersistenceGateway(directory);
            var initial = await gateway.ReadAllAsync();
            Assert.IsTrue(initial.IsSuccess, initial.Error.Message);
            var changed = initial.Value with
            {
                SpeechRecognition = initial.Value.SpeechRecognition with
                {
                    PromptId = "speech-prompt"
                }
            };

            var write = await gateway.WriteAsync(SettingsSection.SpeechRecognition, changed);
            var reread = await gateway.ReadAllAsync();

            Assert.IsTrue(write.IsSuccess, write.Error.Message);
            Assert.IsTrue(reread.IsSuccess, reread.Error.Message);
            Assert.AreEqual("speech-prompt", reread.Value.SpeechRecognition.PromptId);
            StringAssert.Contains(
                await File.ReadAllTextAsync(Path.Combine(directory, "SpeechRecognition.json")),
                "\"PromptId\": \"speech-prompt\"");

            await File.WriteAllTextAsync(
                Path.Combine(directory, "SpeechRecognition.json"),
                "{}");
            var previous = await gateway.ReadAllAsync();

            Assert.IsTrue(previous.IsSuccess, previous.Error.Message);
            Assert.IsNull(previous.Value.SpeechRecognition.PromptId);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task SelectionToolbarExplanation_RoundTripsAndOldFilesDefaultToEnabled()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var gateway = new JsonSettingsPersistenceGateway(directory);
            var initial = await gateway.ReadAllAsync();
            Assert.IsTrue(initial.IsSuccess, initial.Error.Message);
            Assert.IsTrue(initial.Value.SelectionTranslation.ExplanationEnabled);

            var changed = initial.Value with
            {
                SelectionTranslation = initial.Value.SelectionTranslation with
                {
                    ExplanationEnabled = false
                }
            };
            var write = await gateway.WriteAsync(SettingsSection.SelectionTranslation, changed);
            var reread = await gateway.ReadAllAsync();

            Assert.IsTrue(write.IsSuccess, write.Error.Message);
            Assert.IsTrue(reread.IsSuccess, reread.Error.Message);
            Assert.IsFalse(reread.Value.SelectionTranslation.ExplanationEnabled);
            StringAssert.Contains(
                await File.ReadAllTextAsync(Path.Combine(directory, "SelectionTranslation.json")),
                "\"ExplanationEnabled\": false");

            await File.WriteAllTextAsync(
                Path.Combine(directory, "SelectionTranslation.json"),
                "{ \"Enabled\": true }");
            var legacy = await gateway.ReadAllAsync();

            Assert.IsTrue(legacy.IsSuccess, legacy.Error.Message);
            Assert.IsTrue(legacy.Value.SelectionTranslation.ExplanationEnabled);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task SelectionFilter_RoundTripsAndOldFilesDefaultToDisabled()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var gateway = new JsonSettingsPersistenceGateway(directory);
            var initial = await gateway.ReadAllAsync();
            Assert.IsTrue(initial.IsSuccess, initial.Error.Message);
            Assert.AreEqual(SelectionFilterMode.Disabled, initial.Value.SelectionTranslation.FilterMode);
            Assert.IsEmpty(initial.Value.SelectionTranslation.SafeAppList);

            var changed = initial.Value with
            {
                SelectionTranslation = initial.Value.SelectionTranslation with
                {
                    FilterMode = SelectionFilterMode.Whitelist,
                    AppList =
                    [
                        new SelectionAppEntrySettings("chrome.exe", "chrome", "Google Chrome", new ReadOnlyMemory<byte>([1, 2, 3])),
                        new SelectionAppEntrySettings("notepad.exe")
                    ]
                }
            };
            var write = await gateway.WriteAsync(SettingsSection.SelectionTranslation, changed);
            var reread = await gateway.ReadAllAsync();

            Assert.IsTrue(write.IsSuccess, write.Error.Message);
            Assert.IsTrue(reread.IsSuccess, reread.Error.Message);
            Assert.AreEqual(SelectionFilterMode.Whitelist, reread.Value.SelectionTranslation.FilterMode);
            Assert.HasCount(2, reread.Value.SelectionTranslation.SafeAppList);
            Assert.AreEqual("chrome.exe", reread.Value.SelectionTranslation.SafeAppList[0].Identifier);
            Assert.AreEqual("chrome", reread.Value.SelectionTranslation.SafeAppList[0].DisplayName);
            Assert.AreEqual("Google Chrome", reread.Value.SelectionTranslation.SafeAppList[0].Description);
            Assert.IsTrue(reread.Value.SelectionTranslation.SafeAppList[0].IconPng is { IsEmpty: false });
            Assert.AreEqual("notepad.exe", reread.Value.SelectionTranslation.SafeAppList[1].Identifier);
            var json = await File.ReadAllTextAsync(Path.Combine(directory, "SelectionTranslation.json"));
            StringAssert.Contains(json, "\"FilterMode\": 2");
            StringAssert.Contains(json, "\"Google Chrome\"");

            // A legacy identifier-only JSON list must still load as entries without metadata.
            await File.WriteAllTextAsync(
                Path.Combine(directory, "SelectionTranslation.json"),
                "{ \"Enabled\": true, \"AppList\": [\"msedge.exe\"] }");
            var legacy = await gateway.ReadAllAsync();

            Assert.IsTrue(legacy.IsSuccess, legacy.Error.Message);
            Assert.AreEqual(SelectionFilterMode.Disabled, legacy.Value.SelectionTranslation.FilterMode);
            Assert.HasCount(1, legacy.Value.SelectionTranslation.SafeAppList);
            Assert.AreEqual("msedge.exe", legacy.Value.SelectionTranslation.SafeAppList[0].Identifier);
            Assert.IsNull(legacy.Value.SelectionTranslation.SafeAppList[0].DisplayName);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    [DataRow("English")]
    [DataRow("Simplified Chinese")]
    public async Task BuiltInPromptAssets_UseDisplayLanguageOnFirstCreation(
        string displayLanguage)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, "General.json"),
                $$"""
                {
                  "DisplayLanguage": "{{displayLanguage}}"
                }
                """);
            var gateway = new JsonSettingsPersistenceGateway(directory);

            var result = await gateway.ReadAllAsync();

            Assert.IsTrue(result.IsSuccess, result.Error.Message);
            AssertPromptAssetWasImported(
                ReadBuiltInPrompts(displayLanguage),
                result.Value.Prompts);
            var json = await File.ReadAllTextAsync(Path.Combine(directory, "Prompts.json"));
            Assert.IsFalse(json.Contains("BuiltInPromptsSeeded", StringComparison.Ordinal));
            Assert.IsFalse(json.Contains("BuiltInPromptCatalogVersion", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task ExistingPromptSettings_AreNeverSeededOrRestored()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var gateway = new JsonSettingsPersistenceGateway(directory);
            Assert.IsTrue((await gateway.ReadAllAsync()).IsSuccess);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Prompts.json"),
                """
                {
                  "SelectedPromptId": "custom",
                  "BuiltInPromptsSeeded": true,
                  "BuiltInPromptCatalogVersion": 2,
                  "Entries": [
                    {
                      "Id": "custom",
                      "Name": "Custom",
                      "Content": "My custom role.",
                      "IsDefault": true
                    }
                  ]
                }
                """);

            var existing = await gateway.ReadAllAsync();

            Assert.IsTrue(existing.IsSuccess, existing.Error.Message);
            Assert.HasCount(1, existing.Value.Prompts.Entries);
            Assert.AreEqual("custom", existing.Value.Prompts.Entries.Single().Id);

            var deleted = existing.Value with
            {
                Prompts = new PromptSettings(string.Empty, [])
            };
            Assert.IsTrue((await gateway.WriteAsync(SettingsSection.Prompts, deleted)).IsSuccess);

            var reread = await gateway.ReadAllAsync();

            Assert.IsTrue(reread.IsSuccess, reread.Error.Message);
            Assert.IsEmpty(reread.Value.Prompts.Entries);
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task InvalidBuiltInPromptAsset_FailsWithoutCreatingPromptSettings()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var assetsDirectory = Path.Combine(directory, "Assets");
            Directory.CreateDirectory(assetsDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "General.json"),
                "{ \"DisplayLanguage\": \"Simplified Chinese\" }");
            await File.WriteAllTextAsync(
                Path.Combine(assetsDirectory, "builtin.prompt.zh.json"),
                """
                [
                  { "id": "one", "name": "One", "content": "First", "isDefault": true },
                  { "id": "two", "name": "Two", "content": "Second", "isDefault": true }
                ]
                """);
            var gateway = new JsonSettingsPersistenceGateway(
                directory,
                assetsDirectory,
                new PhysicalSettingsFileStore());

            var result = await gateway.ReadAllAsync();

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual("settings.read-failed", result.Error.Code);
            StringAssert.Contains(result.Error.Message, "exactly one default prompt");
            Assert.IsFalse(File.Exists(Path.Combine(directory, "Prompts.json")));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [TestMethod]
    public async Task WriteAsync_ReplacesOnlyTheSelectedFileWithoutLeavingTemporaryFiles()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var gateway = new JsonSettingsPersistenceGateway(directory);
            var read = await gateway.ReadAllAsync();
            Assert.IsTrue(read.IsSuccess, read.Error.Message);
            var before = Directory.GetFiles(directory)
                .Where(path => Path.GetFileName(path) != "Proxy.json")
                .ToDictionary(path => path, File.ReadAllText, StringComparer.Ordinal);
            var changed = read.Value with { Proxy = new ProxySettings("http://127.0.0.1:7890") };

            var write = await gateway.WriteAsync(SettingsSection.Proxy, changed);

            Assert.IsTrue(write.IsSuccess, write.Error.Message);
            StringAssert.Contains(
                File.ReadAllText(Path.Combine(directory, "Proxy.json")),
                "http://127.0.0.1:7890");
            Assert.IsFalse(Directory.EnumerateFiles(directory, "*.tmp").Any());
            foreach (var snapshot in before)
                Assert.AreEqual(snapshot.Value, File.ReadAllText(snapshot.Key));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "EasyChat.RefactorV2.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        var fullPath = Path.GetFullPath(directory);
        var expectedRoot = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "EasyChat.RefactorV2.Tests"));
        if (fullPath.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }

    private static List<BuiltInPromptAssetDefinition> ReadBuiltInPrompts(string? displayLanguage)
    {
        var fileName = string.Equals(
            displayLanguage,
            "Simplified Chinese",
            StringComparison.OrdinalIgnoreCase)
            ? "builtin.prompt.zh.json"
            : "builtin.prompt.en.json";
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        return JsonConvert.DeserializeObject<List<BuiltInPromptAssetDefinition>>(
                   File.ReadAllText(path))
               ?? throw new InvalidOperationException($"Asset '{fileName}' deserialized to null.");
    }

    private static void AssertPromptAssetWasImported(
        IReadOnlyList<BuiltInPromptAssetDefinition> expected,
        PromptSettings actual)
    {
        Assert.HasCount(expected.Count, actual.Entries);
        CollectionAssert.AreEquivalent(
            expected.Select(prompt => prompt.Id).ToArray(),
            actual.Entries.Select(prompt => prompt.Id).ToArray());

        foreach (var expectedPrompt in expected)
        {
            var actualPrompt = actual.Entries.Single(prompt => prompt.Id == expectedPrompt.Id);
            Assert.AreEqual(expectedPrompt.Name, actualPrompt.Name);
            Assert.AreEqual(expectedPrompt.Content, actualPrompt.Content);
            Assert.AreEqual(expectedPrompt.IsDefault, actualPrompt.IsDefault);
        }

        var defaultPrompt = expected.Single(prompt => prompt.IsDefault);
        Assert.AreEqual(defaultPrompt.Id, actual.SelectedPromptId);
    }

    private sealed class BuiltInPromptAssetDefinition
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public bool IsDefault { get; set; }
    }
}
