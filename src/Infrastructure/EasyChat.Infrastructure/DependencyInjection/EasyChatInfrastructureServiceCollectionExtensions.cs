using EasyChat.Contracts.ApplicationData;
using EasyChat.Contracts.AiModels;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings.Persistence;
using EasyChat.Contracts.Speech;
using EasyChat.Contracts.Translation;
using EasyChat.Contracts.Updates;
using EasyChat.Infrastructure.ApplicationData;
using EasyChat.Infrastructure.AiModels;
using EasyChat.Infrastructure.Settings.Persistence;
using EasyChat.Infrastructure.Speech;
using EasyChat.Infrastructure.Speech.EdgeTts;
using EasyChat.Infrastructure.Speech.Recognition;
using EasyChat.Infrastructure.Translation;
using EasyChat.Infrastructure.Updates;
using Microsoft.Extensions.DependencyInjection;

namespace EasyChat.Infrastructure.DependencyInjection;

public static class EasyChatInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddEasyChatInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return AddEasyChatInfrastructure(services, ApplicationDataStore.CreateDefault());
    }

    public static IServiceCollection AddEasyChatInfrastructure(
        this IServiceCollection services,
        string configurationDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);

        return AddEasyChatInfrastructure(
            services,
            ApplicationDataStore.CreateFixed(configurationDirectory));
    }

    private static IServiceCollection AddEasyChatInfrastructure(
        IServiceCollection services,
        ApplicationDataStore applicationData)
    {
        services.AddSingleton(applicationData);
        services.AddSingleton<IApplicationDataPaths>(applicationData);
        services.AddSingleton<IApplicationDataStore>(applicationData);
        services.AddSingleton<ISettingsPersistenceGateway>(
            provider => new JsonSettingsPersistenceGateway(
                () => provider.GetRequiredService<IApplicationDataPaths>().ConfigurationDirectory));
        services.AddHttpClient<IAiModelCatalogTransport, HttpAiModelCatalogTransport>();
        services.AddSingleton<ITranslationProviderFactory, TranslationProviderFactory>();
        services.AddSingleton<ITranslationFailureSink, LoggingTranslationFailureSink>();
        services.AddSingleton<IExternalUriLauncher, ShellExternalUriLauncher>();
        services.AddSingleton<IApplicationUpdateService, VelopackApplicationUpdateService>();
        var assetsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");
        services.AddSingleton<ITtsSynthesisProvider>(_ => new EdgeTtsProvider(assetsDirectory));
        services.AddSingleton<ITtsOutputWriter, FileTtsOutputWriter>();
        services.AddSingleton<ISpeechRecognitionEngine, MicroAsrSpeechRecognitionEngine>();
        services.AddSingleton<MicroAsrSpeechRecognitionModelCatalog>();
        services.AddSingleton<ISpeechRecognitionModelCatalog>(provider =>
            provider.GetRequiredService<MicroAsrSpeechRecognitionModelCatalog>());
        services.AddSingleton<MicroAsrSpeechRecognitionModelInstaller>();
        services.AddSingleton<ISpeechRecognitionModelInstaller>(provider =>
            provider.GetRequiredService<MicroAsrSpeechRecognitionModelInstaller>());
        services.AddSingleton<ISpeechRecognitionModelRemover>(provider =>
            provider.GetRequiredService<MicroAsrSpeechRecognitionModelInstaller>());
        return services;
    }
}
