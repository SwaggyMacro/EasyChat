using System;
using System.Threading;
using System.Threading.Tasks;
using EasyChat.Constants;
using EasyChat.Models.Translation.Selection;
using EasyChat.Services.Abstractions;
using EasyChat.Services.Translation.Ai;
using Microsoft.Extensions.Logging;

#pragma warning disable OPENAI001 // Suppress experimental warning

namespace EasyChat.Services.Translation.Selection;

public class SelectionTranslationProviderManager : ISelectionTranslationProvider
{
    private readonly IConfigurationService _configurationService;
    private readonly AiSelectionTranslationProvider _aiProvider;
    private readonly MachineSelectionTranslationProvider _machineProvider;
    private readonly ILogger<SelectionTranslationProviderManager> _logger;

    public SelectionTranslationProviderManager(
        IConfigurationService configurationService,
        AiSelectionTranslationProvider aiProvider,
        MachineSelectionTranslationProvider machineProvider,
        ILogger<SelectionTranslationProviderManager> logger)
    {
        _configurationService = configurationService;
        _aiProvider = aiProvider;
        _machineProvider = machineProvider;
        _logger = logger;
    }

    public async Task<SelectionTranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default)
    {
        var provider = _configurationService.SelectionTranslation?.Provider ?? Constant.SelectionTranslationProviderType.AiModel;

        if (provider.Equals(Constant.SelectionTranslationProviderType.AiModel, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Using AI Selection Translation Provider");
            return await _aiProvider.TranslateAsync(text, sourceLang, targetLang, cancellationToken);
        }

        if (provider.Equals(Constant.SelectionTranslationProviderType.Machine, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Using Machine Selection Translation Provider");
            return await _machineProvider.TranslateAsync(text, sourceLang, targetLang, cancellationToken);
        }

        _logger.LogWarning("Unknown Selection Translation Provider: {Provider}. Defaulting to AI Provider.", provider);
        return await _aiProvider.TranslateAsync(text, sourceLang, targetLang, cancellationToken);
    }
}
