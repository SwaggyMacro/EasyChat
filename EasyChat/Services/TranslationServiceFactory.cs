using System;
using System.Linq;
using EasyChat.Common;
using EasyChat.Constants;
using EasyChat.Models;
using EasyChat.Services.Abstractions;
using EasyChat.Services.Translation;
using EasyChat.Services.Translation.Ai;
using EasyChat.Services.Translation.Machine;
using EasyChat.Models.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyChat.Services;

/// <summary>
///     Factory for creating translation services based on configuration.
/// </summary>
public class TranslationServiceFactory : ITranslationServiceFactory
{
    private readonly Config _config;
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<TranslationServiceFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public TranslationServiceFactory(
        ILogger<TranslationServiceFactory> logger,
        ILoggerFactory loggerFactory,
        IConfigurationService configurationService)
    {
        _config = Global.Config;
        _configurationService = configurationService;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public ITranslation CreateCurrentService()
    {
        _logger.LogInformation("Creating translation service: Engine={Engine}", _config.GeneralConf.TransEngine);
        
        return _config.GeneralConf.TransEngine switch
        {
            Constant.TransEngineType.Ai => !string.IsNullOrEmpty(_config.GeneralConf.UsingAiModelId) 
                ? CreateAiServiceById(_config.GeneralConf.UsingAiModelId)
                : CreateAiService(_config.GeneralConf.UsingAiModel!),
            Constant.TransEngineType.Machine => !string.IsNullOrEmpty(_config.GeneralConf.UsingMachineTransId)
                ? CreateMachineServiceById(_config.GeneralConf.UsingMachineTransId)
                : CreateMachineService(_config.GeneralConf.UsingMachineTrans!),
            _ => throw new ArgumentException($"Unknown translation engine: {_config.GeneralConf.TransEngine}")
        };
    }

    public ITranslation CreateAiServiceById(string id)
    {
        var model = _config.AiModelConf.ConfiguredModels.FirstOrDefault(m => m.Id == id);
        
        if (model == null)
        {
            _logger.LogWarning("AI model with ID {Id} not found.", id);
             throw new ArgumentException($"Unknown AI model ID: {id}");
        }

        return CreateAiServiceFromModel(model);
    }

    public ITranslation CreateAiService(string modelName)
    {
        var model = _config.AiModelConf.ConfiguredModels.FirstOrDefault(m => m.Name == modelName);
        if (model == null)
            throw new ArgumentException($"Unknown AI model: {modelName}");

        return CreateAiServiceFromModel(model);
    }

    private ITranslation CreateAiServiceFromModel(CustomAiModel model)
    {
        _logger.LogDebug("Creating AI service: Name={Name}, Type={Type}, URL={Url}, Model={Model}", 
            model.Name, model.ModelType, model.ApiUrl, model.Model);

        string apiKey = model.ApiKey;
        string apiUrl = model.ApiUrl;
        string modelId = model.Model;

        var prompt = _configurationService.Prompts?.ActivePromptContent ?? Prompts.DefaultPromptContent;

        return new OpenAiService(
            apiUrl,
            apiKey,
            modelId,
            GetProxyUrl(model.UseProxy),
            prompt,
            _loggerFactory.CreateLogger<OpenAiService>(),
            model.EnableThinking);
    }

    public ITranslation CreateMachineServiceById(string id)
    {
        // Check IDs and return the matching service
        if (_config.MachineTransConf.Baidu.Id == id) return CreateBaiduService();
        if (_config.MachineTransConf.Tencent.Id == id) return CreateTencentService();
        if (_config.MachineTransConf.Google.Id == id) return CreateGoogleService();
        if (_config.MachineTransConf.DeepL.Id == id) return CreateDeepLService();
        
        // Fallback: search by ID if it was possibly updated or if we want to be robust?
        // Actually, the providers are static properties on MachineTransConf. 
        // So checking them one by one is correct.
        
        throw new ArgumentException($"Unknown machine translation provider ID: {id}");
    }

    public ITranslation CreateMachineService(string providerName)
    {
        _logger.LogDebug("Creating machine translation service: Provider={Provider}", providerName);
        
        switch (providerName)
        {
            case "Baidu": return CreateBaiduService();
            case "Tencent": return CreateTencentService();
            case "Google": return CreateGoogleService();
            case "DeepL": return CreateDeepLService();
            default:
                throw new ArgumentException($"Unknown machine translation provider: {providerName}");
        }
    }

    private ITranslation CreateBaiduService()
    {
         var baiduItem = _config.MachineTransConf.Baidu.GetRandomItem();
         return new BaiduService(
             baiduItem.AppId,
             baiduItem.AppKey,
             GetProxyUrl(_config.MachineTransConf.Baidu.UseProxy),
             _loggerFactory.CreateLogger<BaiduService>());
    }

    private ITranslation CreateTencentService()
    {
        var tencentItem = _config.MachineTransConf.Tencent.GetRandomItem();
        return new TencentService(
            tencentItem.SecretId,
            tencentItem.SecretKey,
            GetProxyUrl(_config.MachineTransConf.Tencent.UseProxy),
            _loggerFactory.CreateLogger<TencentService>());
    }

    private ITranslation CreateGoogleService()
    {
        return new GoogleService(
            _config.MachineTransConf.Google.Model,
            _config.MachineTransConf.Google.Key,
            GetProxyUrl(_config.MachineTransConf.Google.UseProxy),
            _loggerFactory.CreateLogger<GoogleService>());
    }

    private ITranslation CreateDeepLService()
    {
        return new DeepLService(
            _config.MachineTransConf.DeepL.ModelType,
            _config.MachineTransConf.DeepL.ApiKey,
            GetProxyUrl(_config.MachineTransConf.DeepL.UseProxy),
            _loggerFactory.CreateLogger<DeepLService>());
    }

    private string? GetProxyUrl(bool useProxy)
    {
        return useProxy && !string.IsNullOrEmpty(_config.ProxyConf.ProxyUrl)
            ? _config.ProxyConf.ProxyUrl
            : null;
    }
}