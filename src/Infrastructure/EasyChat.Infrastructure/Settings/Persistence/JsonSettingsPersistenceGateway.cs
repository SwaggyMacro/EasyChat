using EasyChat.Contracts.Settings.Persistence;
using EasyChat.Contracts.Settings;
using EasyChat.Shared.Results;
using Newtonsoft.Json;

namespace EasyChat.Infrastructure.Settings.Persistence;

public sealed class JsonSettingsPersistenceGateway : ISettingsPersistenceGateway
{
    private const string GeneralFileName = "General.json";
    private const string AiModelFileName = "AiModel.json";
    private const string MachineTranslationFileName = "MachineTrans.json";
    private const string ProxyFileName = "Proxy.json";
    private const string ShortcutFileName = "Shortcut.json";
    private const string PromptsFileName = "Prompts.json";
    private const string ResultFileName = "Result.json";
    private const string InputFileName = "Input.json";
    private const string ScreenshotFileName = "Screenshot.json";
    private const string SpeechRecognitionFileName = "SpeechRecognition.json";
    private const string SelectionTranslationFileName = "SelectionTranslation.json";
    private const string TtsFileName = "Tts.json";
    private const string TextAssistFileName = "TextAssist.json";
    private const string OcrFileName = "Ocr.json";

    private readonly Func<string> _configurationDirectory;
    private readonly ISettingsFileStore _fileStore;

    public JsonSettingsPersistenceGateway(string configurationDirectory)
        : this(() => configurationDirectory, new PhysicalSettingsFileStore())
    {
    }

    internal JsonSettingsPersistenceGateway(Func<string> configurationDirectory)
        : this(configurationDirectory, new PhysicalSettingsFileStore())
    {
    }

    internal JsonSettingsPersistenceGateway(
        string configurationDirectory,
        ISettingsFileStore fileStore)
        : this(() => configurationDirectory, fileStore)
    {
    }

    internal JsonSettingsPersistenceGateway(
        Func<string> configurationDirectory,
        ISettingsFileStore fileStore)
    {
        ArgumentNullException.ThrowIfNull(configurationDirectory);
        ArgumentNullException.ThrowIfNull(fileStore);

        _configurationDirectory = configurationDirectory;
        _fileStore = fileStore;
    }

    public async ValueTask<Result<SettingsBundle>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var dto = new SettingsBundleDto
            {
                General = await ReadAsync<GeneralSettingsDto>(
                    GeneralFileName,
                    cancellationToken).ConfigureAwait(false),
                AiModel = await ReadAsync<AiModelSettingsDto>(
                    AiModelFileName,
                    cancellationToken).ConfigureAwait(false),
                MachineTranslation = await ReadAsync<MachineTranslationSettingsDto>(
                    MachineTranslationFileName,
                    cancellationToken).ConfigureAwait(false),
                Proxy = await ReadAsync<ProxySettingsDto>(ProxyFileName, cancellationToken)
                    .ConfigureAwait(false),
                Shortcut = await ReadAsync<ShortcutSettingsDto>(
                    ShortcutFileName,
                    cancellationToken).ConfigureAwait(false),
                Prompts = await ReadAsync<PromptSettingsDto>(
                    PromptsFileName,
                    cancellationToken).ConfigureAwait(false),
                Result = await ReadAsync<ResultSettingsDto>(ResultFileName, cancellationToken)
                    .ConfigureAwait(false),
                Input = await ReadAsync<InputSettingsDto>(InputFileName, cancellationToken)
                    .ConfigureAwait(false),
                Screenshot = await ReadAsync<ScreenshotSettingsDto>(
                    ScreenshotFileName,
                    cancellationToken).ConfigureAwait(false),
                SpeechRecognition = await ReadAsync<SpeechRecognitionSettingsDto>(
                    SpeechRecognitionFileName,
                    cancellationToken).ConfigureAwait(false),
                SelectionTranslation = await ReadAsync<SelectionTranslationSettingsDto>(
                    SelectionTranslationFileName,
                    cancellationToken).ConfigureAwait(false),
                Tts = await ReadAsync<TtsSettingsDto>(TtsFileName, cancellationToken)
                    .ConfigureAwait(false),
                TextAssist = await ReadAsync<TextAssistSettingsDto>(
                    TextAssistFileName,
                    cancellationToken).ConfigureAwait(false),
                Ocr = await ReadAsync<OcrSettingsDto>(OcrFileName, cancellationToken)
                    .ConfigureAwait(false)
            };

            cancellationToken.ThrowIfCancellationRequested();
            return Result<SettingsBundle>.Success(SettingsPersistenceMapper.ToContract(dto));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Result<SettingsBundle>.Failure(new Error(
                "settings.unavailable",
                "Configuration settings are not available."));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidOperationException
                or FormatException)
        {
            return Result<SettingsBundle>.Failure(new Error(
                "settings.read-failed",
                exception.Message));
        }
    }

    public async ValueTask<Result> WriteAllAsync(
        SettingsBundle settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var dto = SettingsPersistenceMapper.ToDto(settings);
            await WriteAsync(GeneralFileName, dto.General, cancellationToken);
            await WriteAsync(AiModelFileName, dto.AiModel, cancellationToken);
            await WriteAsync(
                MachineTranslationFileName,
                dto.MachineTranslation,
                cancellationToken);
            await WriteAsync(ProxyFileName, dto.Proxy, cancellationToken);
            await WriteAsync(ShortcutFileName, dto.Shortcut, cancellationToken);
            await WriteAsync(PromptsFileName, dto.Prompts, cancellationToken);
            await WriteAsync(ResultFileName, dto.Result, cancellationToken);
            await WriteAsync(InputFileName, dto.Input, cancellationToken);
            await WriteAsync(ScreenshotFileName, dto.Screenshot, cancellationToken);
            await WriteAsync(
                SpeechRecognitionFileName,
                dto.SpeechRecognition,
                cancellationToken);
            await WriteAsync(
                SelectionTranslationFileName,
                dto.SelectionTranslation,
                cancellationToken);
            await WriteAsync(TtsFileName, dto.Tts, cancellationToken);
            await WriteAsync(TextAssistFileName, dto.TextAssist, cancellationToken);
            await WriteAsync(OcrFileName, dto.Ocr, cancellationToken);
            return Result.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return Result.Failure(new Error("settings.write-failed", exception.Message));
        }
    }

    public async ValueTask<Result> WriteAsync(
        SettingsSection section,
        SettingsBundle settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var dto = SettingsPersistenceMapper.ToDto(settings);
            switch (section)
            {
                case SettingsSection.General:
                    await WriteAsync(GeneralFileName, dto.General, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case SettingsSection.AiModel:
                    await WriteAsync(AiModelFileName, dto.AiModel, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case SettingsSection.MachineTranslation:
                    await WriteAsync(
                            MachineTranslationFileName,
                            dto.MachineTranslation,
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case SettingsSection.Proxy:
                    await WriteAsync(ProxyFileName, dto.Proxy, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case SettingsSection.Shortcut:
                    await WriteAsync(ShortcutFileName, dto.Shortcut, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case SettingsSection.Prompts:
                    await WriteAsync(PromptsFileName, dto.Prompts, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case SettingsSection.Result:
                    await WriteAsync(ResultFileName, dto.Result, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case SettingsSection.Input:
                    await WriteAsync(InputFileName, dto.Input, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case SettingsSection.Screenshot:
                    await WriteAsync(ScreenshotFileName, dto.Screenshot, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case SettingsSection.SpeechRecognition:
                    await WriteAsync(
                            SpeechRecognitionFileName,
                            dto.SpeechRecognition,
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case SettingsSection.SelectionTranslation:
                    await WriteAsync(
                            SelectionTranslationFileName,
                            dto.SelectionTranslation,
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case SettingsSection.Tts:
                    await WriteAsync(TtsFileName, dto.Tts, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case SettingsSection.TextAssist:
                    await WriteAsync(TextAssistFileName, dto.TextAssist, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case SettingsSection.Ocr:
                    await WriteAsync(OcrFileName, dto.Ocr, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(section), section, null);
            }

            return Result.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return Result.Failure(new Error("settings.write-failed", exception.Message));
        }
    }

    private async ValueTask<T> ReadAsync<T>(
        string fileName,
        CancellationToken cancellationToken)
        where T : new()
    {
        var path = Path.Combine(ConfigurationDirectory, fileName);
        string json;
        try
        {
            json = await _fileStore.ReadAllTextAsync(path, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            var settings = new T();
            await WriteAsync(fileName, settings, cancellationToken).ConfigureAwait(false);
            return settings;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var dto = JsonConvert.DeserializeObject<T>(json)
                  ?? throw new JsonSerializationException(
                      $"Configuration file '{fileName}' deserialized to null.");
        cancellationToken.ThrowIfCancellationRequested();
        return dto;
    }

    private async ValueTask WriteAsync<T>(
        string fileName,
        T settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
        cancellationToken.ThrowIfCancellationRequested();
        await _fileStore.WriteAllTextAsync(
            Path.Combine(ConfigurationDirectory, fileName),
            json,
            cancellationToken).ConfigureAwait(false);
    }

    private string ConfigurationDirectory => Path.GetFullPath(_configurationDirectory());
}
