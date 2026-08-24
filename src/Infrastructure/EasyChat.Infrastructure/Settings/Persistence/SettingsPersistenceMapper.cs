using EasyChat.Contracts.Ocr;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.ImageTranslation;
using EasyChat.Contracts.Translation;

namespace EasyChat.Infrastructure.Settings.Persistence;

internal static class SettingsPersistenceMapper
{
    public static SettingsBundle ToContract(SettingsBundleDto source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new SettingsBundle(
            ToContract(source.General),
            ToContract(source.AiModel),
            ToContract(source.MachineTranslation),
            ToContract(source.Proxy),
            ToContract(source.Shortcut),
            ToContract(source.Prompts),
            ToContract(source.Result),
            ToContract(source.Input),
            ToContract(source.Screenshot),
            ToContract(source.SpeechRecognition),
            ToContract(source.SelectionTranslation),
            ToContract(source.Tts),
            ToContract(source.TextAssist),
            ToContract(source.Ocr));
    }

    public static SettingsBundleDto ToDto(SettingsBundle source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new SettingsBundleDto
        {
            General = ToDto(source.General),
            AiModel = ToDto(source.AiModel),
            MachineTranslation = ToDto(source.MachineTranslation),
            Proxy = ToDto(source.Proxy),
            Shortcut = ToDto(source.Shortcut),
            Prompts = ToDto(source.Prompts),
            Result = ToDto(source.Result),
            Input = ToDto(source.Input),
            Screenshot = ToDto(source.Screenshot),
            SpeechRecognition = ToDto(source.SpeechRecognition),
            SelectionTranslation = ToDto(source.SelectionTranslation),
            Tts = ToDto(source.Tts),
            TextAssist = ToDto(source.TextAssist),
            Ocr = ToDto(source.Ocr)
        };
    }

    private static LanguageSettings ToContract(LanguageSettingsDto source)
    {
        var localizedName = string.Equals(
            System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            "zh",
            StringComparison.OrdinalIgnoreCase)
            ? source.ChineseName
            : source.EnglishName;
        return new LanguageSettings(
            source.Id,
            source.ChineseName,
            source.EnglishName,
            source.Icon,
            localizedName,
            localizedName,
            new Dictionary<string, string>(source.ProviderCodes, StringComparer.Ordinal));
    }

    private static LanguageSettingsDto ToDto(LanguageSettings source) => new()
    {
        Id = source.Id,
        ChineseName = source.ChineseName,
        EnglishName = source.EnglishName,
        Icon = source.Icon,
        LocalizedName = source.LocalizedName,
        DisplayName = source.DisplayName,
        ProviderCodes = source.ProviderCodes.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.Ordinal)
    };

    private static GeneralSettings ToContract(GeneralSettingsDto source)
    {
        var customThemes = (source.CustomColorThemes ?? [])
            .Where(theme => !string.IsNullOrWhiteSpace(theme.Id)
                            && !string.IsNullOrWhiteSpace(theme.DisplayName)
                            && !string.IsNullOrWhiteSpace(theme.PrimaryColor)
                            && !string.IsNullOrWhiteSpace(theme.AccentColor))
            .Select(ToContract)
            .ToArray();
        var activeTheme = source.ColorTheme;

        // Versions before the theme catalog stored only the active custom palette.
        // Materialize it as a catalog entry while preserving the selected theme.
        if (customThemes.Length == 0
            && !string.IsNullOrWhiteSpace(source.ColorTheme)
            && !string.IsNullOrWhiteSpace(source.CustomThemePrimaryColor)
            && !string.IsNullOrWhiteSpace(source.CustomThemeAccentColor))
        {
            var legacyThemeId = $"legacy-custom:{source.ColorTheme}";
            customThemes =
            [
                new CustomColorThemeSettings(
                    legacyThemeId,
                    source.ColorTheme,
                    source.CustomThemePrimaryColor,
                    source.CustomThemeAccentColor)
            ];
            activeTheme = legacyThemeId;
        }

        return new GeneralSettings(
            ToContract(source.SourceLanguage),
            ToContract(source.TargetLanguage),
            source.DisplayLanguage,
            source.NativeLanguage is null ? null : ToContract(source.NativeLanguage),
            (ClosingBehavior)(int)source.ClosingBehavior,
            source.TransEngine,
            source.UsingAiModel,
            source.UsingAiModelId,
            source.UsingMachineTransId,
            source.UsingMachineTrans,
            ToThemeMode(source.BaseTheme),
            activeTheme,
            source.CustomThemePrimaryColor,
            source.CustomThemeAccentColor,
            source.TitleBarVisible,
            source.FullScreen)
        {
            CustomColorThemes = customThemes
        };
    }

    private static GeneralSettingsDto ToDto(GeneralSettings source) => new()
    {
        SourceLanguage = ToDto(source.SourceLanguage),
        TargetLanguage = ToDto(source.TargetLanguage),
        DisplayLanguage = source.DisplayLanguage,
        NativeLanguage = source.NativeLanguage is null ? null : ToDto(source.NativeLanguage),
        ClosingBehavior = (ClosingBehaviorDto)(int)source.ClosingBehavior,
        TransEngine = source.TranslationEngine,
        UsingAiModel = source.AiModel,
        UsingAiModelId = source.AiModelId,
        UsingMachineTransId = source.MachineTranslationId,
        UsingMachineTrans = source.MachineTranslation,
        BaseTheme = ToPersistenceValue(source.BaseTheme),
        ColorTheme = source.ColorTheme,
        CustomThemePrimaryColor = source.CustomThemePrimaryColor,
        CustomThemeAccentColor = source.CustomThemeAccentColor,
        CustomColorThemes = source.CustomColorThemes.Select(ToDto).ToList(),
        TitleBarVisible = source.TitleBarVisible,
        FullScreen = source.FullScreen
    };

    private static CustomColorThemeSettings ToContract(CustomColorThemeSettingsDto source) => new(
        source.Id,
        source.DisplayName,
        source.PrimaryColor,
        source.AccentColor);

    private static CustomColorThemeSettingsDto ToDto(CustomColorThemeSettings source) => new()
    {
        Id = source.Id,
        DisplayName = source.DisplayName,
        PrimaryColor = source.PrimaryColor,
        AccentColor = source.AccentColor
    };

    private static ThemeMode ToThemeMode(string? value)
    {
        if (string.Equals(value, "Light", StringComparison.OrdinalIgnoreCase))
            return ThemeMode.Light;
        if (string.Equals(value, "Dark", StringComparison.OrdinalIgnoreCase))
            return ThemeMode.Dark;
        return ThemeMode.System;
    }

    private static string ToPersistenceValue(ThemeMode value) => value switch
    {
        ThemeMode.Light => "Light",
        ThemeMode.Dark => "Dark",
        _ => "Default"
    };

    private static AiModelSettings ToContract(AiModelSettingsDto source) => new(
        source.ConfiguredModels.Select(ToContract).ToArray());

    private static AiModelSettingsDto ToDto(AiModelSettings source) => new()
    {
        ConfiguredModels = source.ConfiguredModels.Select(ToDto).ToList()
    };

    private static CustomAiModelSettings ToContract(CustomAiModelSettingsDto source) => new(
        source.Id,
        source.Name,
        (AiModelType)(int)source.ModelType,
        source.ApiKeys.ToArray(),
        source.ApiUrl,
        source.Model,
        source.UseProxy,
        source.EnableThinking);

    private static CustomAiModelSettingsDto ToDto(CustomAiModelSettings source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        ModelType = (AiModelTypeDto)(int)source.ModelType,
        ApiKeys = source.ApiKeys.ToList(),
        ApiUrl = source.ApiUrl,
        Model = source.Model,
        UseProxy = source.UseProxy,
        EnableThinking = source.EnableThinking
    };

    private static MachineTranslationSettings ToContract(MachineTranslationSettingsDto source) => new(
        ToContract(source.Baidu),
        ToContract(source.Tencent),
        ToContract(source.Google),
        ToContract(source.DeepL));

    private static MachineTranslationSettingsDto ToDto(MachineTranslationSettings source) => new()
    {
        Baidu = ToDto(source.Baidu),
        Tencent = ToDto(source.Tencent),
        Google = ToDto(source.Google),
        DeepL = ToDto(source.DeepL)
    };

    private static BaiduTranslationSettings ToContract(BaiduTranslationSettingsDto source) => new(
        source.UseProxy,
        source.Id,
        source.Items.Select(ToContract).ToArray());

    private static BaiduTranslationSettingsDto ToDto(BaiduTranslationSettings source) => new()
    {
        UseProxy = source.UseProxy,
        Id = source.Id,
        Items = source.Items.Select(ToDto).ToList()
    };

    private static BaiduCredentialSettings ToContract(BaiduCredentialSettingsDto source) =>
        new(source.AppId, source.AppKey);

    private static BaiduCredentialSettingsDto ToDto(BaiduCredentialSettings source) => new()
    {
        AppId = source.AppId,
        AppKey = source.AppKey
    };

    private static TencentTranslationSettings ToContract(TencentTranslationSettingsDto source) => new(
        source.UseProxy,
        source.Id,
        source.Items.Select(ToContract).ToArray());

    private static TencentTranslationSettingsDto ToDto(TencentTranslationSettings source) => new()
    {
        UseProxy = source.UseProxy,
        Id = source.Id,
        Items = source.Items.Select(ToDto).ToList()
    };

    private static TencentCredentialSettings ToContract(TencentCredentialSettingsDto source) =>
        new(source.SecretId, source.SecretKey);

    private static TencentCredentialSettingsDto ToDto(TencentCredentialSettings source) => new()
    {
        SecretId = source.SecretId,
        SecretKey = source.SecretKey
    };

    private static GoogleTranslationSettings ToContract(GoogleTranslationSettingsDto source) => new(
        source.UseProxy,
        source.Id,
        source.Model,
        source.ApiKeys.ToArray());

    private static GoogleTranslationSettingsDto ToDto(GoogleTranslationSettings source) => new()
    {
        UseProxy = source.UseProxy,
        Id = source.Id,
        Model = source.Model,
        ApiKeys = source.ApiKeys.ToList()
    };

    private static DeepLTranslationSettings ToContract(DeepLTranslationSettingsDto source) => new(
        source.UseProxy,
        source.Id,
        source.ModelType,
        source.ApiKeys.ToArray());

    private static DeepLTranslationSettingsDto ToDto(DeepLTranslationSettings source) => new()
    {
        UseProxy = source.UseProxy,
        Id = source.Id,
        ModelType = source.ModelType,
        ApiKeys = source.ApiKeys.ToList()
    };

    private static ProxySettings ToContract(ProxySettingsDto source) => new(
        source.Mode ?? (string.IsNullOrWhiteSpace(source.ProxyUrl)
            ? NetworkProxyMode.System
            : NetworkProxyMode.Custom),
        source.ProxyUrl);

    private static ProxySettingsDto ToDto(ProxySettings source) => new()
    {
        Mode = source.Mode,
        ProxyUrl = source.ProxyUrl
    };

    private static ShortcutSettings ToContract(ShortcutSettingsDto source) => new(
        source.Entries.Select(ToContract).ToArray());

    private static ShortcutSettingsDto ToDto(ShortcutSettings source) => new()
    {
        Entries = source.Entries.Select(ToDto).ToList()
    };

    private static ShortcutEntrySettings ToContract(ShortcutEntrySettingsDto source) => new(
        source.ActionType,
        source.Parameter is null ? null : ToContract(source.Parameter),
        source.KeyCombination,
        source.IsEnabled,
        source.Remark);

    private static ShortcutEntrySettingsDto ToDto(ShortcutEntrySettings source) => new()
    {
        ActionType = source.ActionType,
        Parameter = source.Parameter is null ? null : ToDto(source.Parameter),
        KeyCombination = source.KeyCombination,
        IsEnabled = source.IsEnabled,
        Remark = source.Remark
    };

    private static ShortcutParameterSettings ToContract(ShortcutParameterSettingsDto source) => new(
        source.Engine,
        source.EngineId,
        source.Source is null ? null : ToContract(source.Source),
        source.Target is null ? null : ToContract(source.Target),
        source.Value,
        source.ReadSelectedText,
        source.InputTranslateBeforeKey,
        source.InputTranslateAfterKey,
        source.ReplaceCurrentInput,
        source.TextAssistMode is null
            ? null
            : (TextAssistShortcutMode)(int)source.TextAssistMode.Value,
        source.ShowSelectionToolbar);

    private static ShortcutParameterSettingsDto ToDto(ShortcutParameterSettings source) => new()
    {
        Engine = source.Engine,
        EngineId = source.EngineId,
        Source = source.Source is null ? null : ToDto(source.Source),
        Target = source.Target is null ? null : ToDto(source.Target),
        Value = source.Value,
        ReadSelectedText = source.ReadSelectedText,
        InputTranslateBeforeKey = source.InputTranslateBeforeKey,
        InputTranslateAfterKey = source.InputTranslateAfterKey,
        ReplaceCurrentInput = source.ReplaceCurrentInput,
        TextAssistMode = source.TextAssistMode is null
            ? null
            : (TextAssistShortcutModeDto)(int)source.TextAssistMode.Value,
        ShowSelectionToolbar = source.ShowSelectionToolbar
    };

    private static PromptSettings ToContract(PromptSettingsDto source) => new(
        source.SelectedPromptId,
        source.Entries.Select(ToContract).ToArray());

    private static PromptSettingsDto ToDto(PromptSettings source) => new()
    {
        SelectedPromptId = source.SelectedPromptId,
        Entries = source.Entries.Select(ToDto).ToList()
    };

    private static PromptEntrySettings ToContract(PromptEntrySettingsDto source) => new(
        source.Id,
        source.Name,
        source.Content,
        source.IsDefault);

    private static PromptEntrySettingsDto ToDto(PromptEntrySettings source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Content = source.Content,
        IsDefault = source.IsDefault
    };

    private static ResultSettings ToContract(ResultSettingsDto source) => new(
        source.AutoCloseDelay,
        source.FontSize,
        source.EnableAutoReadDelay,
        source.MsPerChar,
        source.TransparencyLevel,
        source.BackgroundColor,
        source.FontColor,
        source.FontFamily,
        source.WindowBackgroundColor,
        (ResultWindowMode)(int)source.ScreenshotResultMode,
        (ResultReadAloudMode)(int)source.ReadAloudMode);

    private static ResultSettingsDto ToDto(ResultSettings source) => new()
    {
        AutoCloseDelay = source.AutoCloseDelay,
        FontSize = source.FontSize,
        EnableAutoReadDelay = source.EnableAutoReadDelay,
        MsPerChar = source.MillisecondsPerCharacter,
        TransparencyLevel = source.TransparencyLevel,
        BackgroundColor = source.BackgroundColor,
        FontColor = source.FontColor,
        FontFamily = source.FontFamily,
        WindowBackgroundColor = source.WindowBackgroundColor,
        ScreenshotResultMode = (ResultWindowModeDto)(int)source.ScreenshotResultMode,
        ReadAloudMode = (ResultReadAloudModeDto)(int)source.ReadAloudMode
    };

    private static InputSettings ToContract(InputSettingsDto source) => new(
        source.TransparencyLevel,
        source.BackgroundColor,
        source.FontColor,
        source.KeySendDelay,
        (InputDeliveryMode)(int)source.DeliveryMode,
        source.ReverseTranslateLanguage,
        source.TypingSourceLanguage,
        source.TypingTargetLanguage,
        source.FollowGlobalLanguage,
        (InputTranslationMode)(int)source.TranslationMode);

    private static InputSettingsDto ToDto(InputSettings source) => new()
    {
        TransparencyLevel = source.TransparencyLevel,
        BackgroundColor = source.BackgroundColor,
        FontColor = source.FontColor,
        KeySendDelay = source.KeySendDelay,
        DeliveryMode = (InputDeliveryModeDto)(int)source.DeliveryMode,
        ReverseTranslateLanguage = source.ReverseTranslateLanguage,
        TypingSourceLanguage = source.TypingSourceLanguage,
        TypingTargetLanguage = source.TypingTargetLanguage,
        FollowGlobalLanguage = source.FollowGlobalLanguage,
        TranslationMode = (InputTranslationModeDto)(int)source.TranslationMode
    };

    private static ScreenshotSettings ToContract(ScreenshotSettingsDto source) => new(
        source.Mode,
        source.FixedAreas.Select(ToContract).ToArray(),
        (OcrRecognitionMode)(int)source.OcrMode,
        Math.Clamp(
            source.OcrIdleTimeoutSeconds,
            ScreenshotSettings.MinOcrIdleTimeoutSeconds,
            ScreenshotSettings.MaxOcrIdleTimeoutSeconds),
        source.ClosePreviousOcrWindow,
        Enum.IsDefined(source.ImageTextEraseMode)
            ? (ImageTextEraseMode)(int)source.ImageTextEraseMode
            : ImageTextEraseMode.Fast);

    private static ScreenshotSettingsDto ToDto(ScreenshotSettings source) => new()
    {
        Mode = source.Mode,
        FixedAreas = source.FixedAreas.Select(ToDto).ToList(),
        OcrMode = (OcrRecognitionModeDto)(int)source.OcrMode,
        OcrIdleTimeoutSeconds = Math.Clamp(
            source.OcrIdleTimeoutSeconds,
            ScreenshotSettings.MinOcrIdleTimeoutSeconds,
            ScreenshotSettings.MaxOcrIdleTimeoutSeconds),
        ClosePreviousOcrWindow = source.ClosePreviousOcrWindow,
        ImageTextEraseMode = (ImageTextEraseModeDto)(int)source.ImageTextEraseMode
    };

    private static FixedAreaSettings ToContract(FixedAreaSettingsDto source) => new(
        source.Id,
        source.Name,
        source.X,
        source.Y,
        source.Width,
        source.Height,
        source.IsEnabled);

    private static FixedAreaSettingsDto ToDto(FixedAreaSettings source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        X = source.X,
        Y = source.Y,
        Width = source.Width,
        Height = source.Height,
        IsEnabled = source.IsEnabled
    };

    private static SpeechRecognitionSettings ToContract(SpeechRecognitionSettingsDto source)
    {
        var legacy = new SpeechRecognitionSettings(
            source.RecognitionLanguage,
            source.IsTranslationEnabled,
            source.IsRealTimePreviewEnabled,
            source.TargetLanguage,
            source.EngineId,
            source.EngineType,
            source.MaxSentencesPerLine,
            (FloatingDisplayMode)(int)source.FloatingDisplayMode,
            source.MaxFloatingHistory,
            source.AutoClearInterval,
            (SubtitleSource)(int)source.MainSubtitleSource,
            source.FontSize,
            source.FontFamily,
            source.FontColor,
            (SubtitleSource)(int)source.SecondarySubtitleSource,
            source.SecondaryFontSize,
            source.SecondaryFontFamily,
            source.SecondaryFontColor,
            source.BackgroundColor,
            source.SubtitleBackgroundColor,
            source.WindowOpacity,
            source.IsFloatingWindowLocked,
            source.FloatingWindowOrientation,
            source.WindowX,
            source.WindowY,
            source.WindowWidth,
            source.WindowHeight,
            source.PromptId,
            source.IsTranslatedSpeechEnabled);

        var audio = source.AudioTranslationConfiguration is not null
            ? ToContract(source.AudioTranslationConfiguration)
            : ToSpeechConfiguration(legacy);
        var realtime = source.RealtimeInterpretationConfiguration is not null
            ? ToContract(source.RealtimeInterpretationConfiguration)
            : audio with
            {
                IsTranslationEnabled = true,
                IsTranslatedSpeechEnabled = true
            };
        if (source.FollowGlobalTranslationConfiguration == true)
        {
            audio = FollowGlobalSpeechConfiguration(audio);
            realtime = FollowGlobalSpeechConfiguration(realtime);
        }

        return legacy with
        {
            EngineId = audio.EngineId,
            EngineType = audio.EngineType,
            PromptId = audio.PromptId,
            IsTranslationEnabled = audio.IsTranslationEnabled,
            IsRealTimePreviewEnabled = audio.IsRealTimePreviewEnabled,
            TargetLanguage = audio.TargetLanguage,
            IsTranslatedSpeechEnabled = audio.IsTranslatedSpeechEnabled,
            AudioTranslationConfiguration = audio,
            RealtimeInterpretationConfiguration = realtime
        };
    }

    private static SpeechRecognitionSettingsDto ToDto(SpeechRecognitionSettings source)
    {
        // The legacy top-level fields represent the active audio-translation tab.
        // Keep the new per-tab record synchronized so callers that update the
        // compatibility fields cannot be overwritten by a stale nested snapshot.
        var audio = (source.AudioTranslationConfiguration ?? ToSpeechConfiguration(source)) with
        {
            RecognitionLanguage = source.RecognitionLanguage,
            IsTranslationEnabled = source.IsTranslationEnabled,
            IsTranslatedSpeechEnabled = source.IsTranslatedSpeechEnabled,
            IsRealTimePreviewEnabled = source.IsRealTimePreviewEnabled,
            TargetLanguage = source.TargetLanguage,
            EngineId = source.EngineId,
            EngineType = source.EngineType,
            PromptId = source.PromptId
        };
        var realtime = source.RealtimeInterpretationConfiguration
            ?? ToSpeechConfiguration(source) with
            {
                IsTranslationEnabled = true,
                IsTranslatedSpeechEnabled = true
            };

        return new SpeechRecognitionSettingsDto
        {
        RecognitionLanguage = source.RecognitionLanguage,
        IsTranslationEnabled = source.IsTranslationEnabled,
        IsRealTimePreviewEnabled = source.IsRealTimePreviewEnabled,
        TargetLanguage = source.TargetLanguage,
        EngineId = source.EngineId,
        EngineType = source.EngineType,
        PromptId = source.PromptId,
        AudioTranslationConfiguration = ToDto(audio),
        RealtimeInterpretationConfiguration = ToDto(realtime),
        MaxSentencesPerLine = source.MaxSentencesPerLine,
        FloatingDisplayMode = (FloatingDisplayModeDto)(int)source.FloatingDisplayMode,
        MaxFloatingHistory = source.MaxFloatingHistory,
        AutoClearInterval = source.AutoClearInterval,
        MainSubtitleSource = (SubtitleSourceDto)(int)source.MainSubtitleSource,
        FontSize = source.PrimaryFontSize,
        FontFamily = source.PrimaryFontFamily,
        FontColor = source.PrimaryFontColor,
        SecondarySubtitleSource = (SubtitleSourceDto)(int)source.SecondarySubtitleSource,
        SecondaryFontSize = source.SecondaryFontSize,
        SecondaryFontFamily = source.SecondaryFontFamily,
        SecondaryFontColor = source.SecondaryFontColor,
        BackgroundColor = source.BackgroundColor,
        SubtitleBackgroundColor = source.SubtitleBackgroundColor,
        WindowOpacity = source.WindowOpacity,
        IsFloatingWindowLocked = source.IsFloatingWindowLocked,
        FloatingWindowOrientation = source.FloatingWindowOrientation,
        WindowX = source.WindowX,
        WindowY = source.WindowY,
        WindowWidth = source.WindowWidth,
        WindowHeight = source.WindowHeight,
        IsTranslatedSpeechEnabled = source.IsTranslatedSpeechEnabled
        };
    }

    private static SpeechTranslationConfiguration ToContract(SpeechTranslationConfigurationDto source) => new(
        source.RecognitionLanguage,
        source.IsTranslationEnabled,
        source.IsTranslatedSpeechEnabled,
        source.IsRealTimePreviewEnabled,
        source.TargetLanguage,
        source.EngineId,
        source.EngineType,
        source.PromptId);

    private static SpeechTranslationConfigurationDto ToDto(SpeechTranslationConfiguration source) => new()
    {
        RecognitionLanguage = source.RecognitionLanguage,
        IsTranslationEnabled = source.IsTranslationEnabled,
        IsTranslatedSpeechEnabled = source.IsTranslatedSpeechEnabled,
        IsRealTimePreviewEnabled = source.IsRealTimePreviewEnabled,
        TargetLanguage = source.TargetLanguage,
        EngineId = source.EngineId,
        EngineType = source.EngineType,
        PromptId = source.PromptId
    };

    private static SpeechTranslationConfiguration ToSpeechConfiguration(SpeechRecognitionSettings source) => new(
        source.RecognitionLanguage,
        source.IsTranslationEnabled,
        source.IsTranslatedSpeechEnabled,
        source.IsRealTimePreviewEnabled,
        source.TargetLanguage,
        source.EngineId,
        source.EngineType,
        source.PromptId);

    private static SpeechTranslationConfiguration FollowGlobalSpeechConfiguration(
        SpeechTranslationConfiguration source) => source with
        {
            EngineId = TranslationConfigurationOptionIds.FollowGlobal,
            PromptId = TranslationConfigurationOptionIds.FollowGlobal
        };

    private static string NormalizeTranslationEngine(string? value) =>
        value switch
        {
            "AI" => TranslationEngineNames.AiModel,
            "Machine" => TranslationEngineNames.MachineTrans,
            null or "" => TranslationConfigurationOptionIds.FollowGlobal,
            _ => value
        };

    private static SelectionTranslationSettings ToContract(
        SelectionTranslationSettingsDto source)
    {
        var entries = source.AppEntries.Count > 0
            ? source.AppEntries.Select(ToContract).ToArray()
            : source.AppList
                .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(identifier => new SelectionAppEntrySettings(identifier))
                .ToArray();
        var provider = source.FollowGlobalTranslationConfiguration == true
            ? TranslationConfigurationOptionIds.FollowGlobal
            : NormalizeTranslationEngine(source.Provider);
        var machineProvider = source.FollowGlobalTranslationConfiguration == true
            ? TranslationConfigurationOptionIds.FollowGlobal
            : source.MachineProvider;
        var aiModelId = source.FollowGlobalTranslationConfiguration == true
            ? TranslationConfigurationOptionIds.FollowGlobal
            : source.AiModelId;
        var promptId = source.FollowGlobalTranslationConfiguration == true
            ? TranslationConfigurationOptionIds.FollowGlobal
            : source.PromptId;
        return new SelectionTranslationSettings(
            source.Enabled,
            provider,
            machineProvider,
            aiModelId,
            promptId,
            (SelectionTriggerMode)(int)source.TriggerMode,
            source.TranslationEnabled,
            source.CorrectionEnabled,
            source.PolishEnabled,
            source.SummaryEnabled,
            source.ExplanationEnabled ?? true,
            (SelectionFilterMode)(int)source.FilterMode,
            entries);
    }

    private static SelectionAppEntrySettings ToContract(SelectionAppEntryDto source) => new(
        source.Identifier,
        source.DisplayName,
        source.Description,
        source.IconPng is null ? null : new ReadOnlyMemory<byte>(source.IconPng));

    private static SelectionTranslationSettingsDto ToDto(
        SelectionTranslationSettings source) => new()
        {
            Enabled = source.Enabled,
            Provider = source.Provider,
            MachineProvider = source.MachineProvider,
            AiModelId = source.AiModelId,
            PromptId = source.PromptId,
            TriggerMode = (SelectionTriggerModeDto)(int)source.TriggerMode,
            TranslationEnabled = source.TranslationEnabled,
            CorrectionEnabled = source.CorrectionEnabled,
            PolishEnabled = source.PolishEnabled,
            SummaryEnabled = source.SummaryEnabled,
            ExplanationEnabled = source.ExplanationEnabled,
            FilterMode = (SelectionFilterModeDto)(int)source.FilterMode,
            AppEntries = source.SafeAppList.Select(ToDto).ToList(),
            AppList = source.SafeAppList.Select(entry => entry.Identifier).ToList()
        };

    private static SelectionAppEntryDto ToDto(SelectionAppEntrySettings source) => new()
    {
        Identifier = source.Identifier,
        DisplayName = source.DisplayName,
        Description = source.Description,
        IconPng = source.IconPng is { IsEmpty: false } bytes ? bytes.ToArray() : null
    };

    private static TtsSettings ToContract(TtsSettingsDto source) => new(
        source.Provider,
        source.ProviderVoicePreferences.ToDictionary(
            provider => provider.Key,
            provider => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(
                provider.Value,
                StringComparer.Ordinal),
            StringComparer.Ordinal));

    private static TtsSettingsDto ToDto(TtsSettings source) => new()
    {
        Provider = source.Provider,
        ProviderVoicePreferences = source.ProviderVoicePreferences.ToDictionary(
            provider => provider.Key,
            provider => provider.Value.ToDictionary(
                voice => voice.Key,
                voice => voice.Value,
                StringComparer.Ordinal),
            StringComparer.Ordinal)
    };

    private static TextAssistSettings ToContract(TextAssistSettingsDto source)
    {
        var provider = NormalizeTranslationEngine(source.Provider);
        var aiModelId = source.AiModelId;
        var translationPromptId = source.TranslationPromptId;
        var correctionPromptId = source.CorrectionPromptId;
        var polishPromptId = source.PolishPromptId;
        var summaryPromptId = source.SummaryPromptId;
        var machineProvider = source.MachineProvider;
        if (source.FollowGlobal == true)
        {
            provider = aiModelId = translationPromptId = correctionPromptId =
                polishPromptId = summaryPromptId = machineProvider =
                TranslationConfigurationOptionIds.FollowGlobal;
        }

        return new TextAssistSettings(
        source.SourceLanguageId,
        source.TargetLanguageId,
        provider,
        aiModelId,
        translationPromptId,
        correctionPromptId,
        polishPromptId,
        summaryPromptId,
        source.DetailedExplanation,
        source.TranslationConfigurationExpanded,
        source.CorrectionConfigurationExpanded,
        machineProvider);
    }

    private static TextAssistSettingsDto ToDto(TextAssistSettings source) => new()
    {
        // FollowGlobal is a legacy compatibility flag. The normalized contract
        // stores inheritance through the individual option identifiers instead.
        FollowGlobal = null,
        SourceLanguageId = source.SourceLanguageId,
        TargetLanguageId = source.TargetLanguageId,
        Provider = source.Provider,
        AiModelId = source.AiModelId,
        TranslationPromptId = source.TranslationPromptId,
        CorrectionPromptId = source.CorrectionPromptId,
        PolishPromptId = source.PolishPromptId,
        SummaryPromptId = source.SummaryPromptId,
        DetailedExplanation = source.DetailedExplanation,
        TranslationConfigurationExpanded = source.TranslationConfigurationExpanded,
        CorrectionConfigurationExpanded = source.CorrectionConfigurationExpanded,
        MachineProvider = source.MachineProvider
    };

    private static OcrSettings ToContract(OcrSettingsDto source) => new(source.UseProxy);

    private static OcrSettingsDto ToDto(OcrSettings source) => new()
    {
        UseProxy = source.UseProxy
    };
}
