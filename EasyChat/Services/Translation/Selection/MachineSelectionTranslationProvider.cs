using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyChat.Constants;
using EasyChat.Models.Translation.Selection;
using EasyChat.Services.Abstractions;
using EasyChat.Services.Languages;
using Microsoft.Extensions.Logging;

namespace EasyChat.Services.Translation.Selection;

public class MachineSelectionTranslationProvider : ISelectionTranslationProvider
{
    private readonly IConfigurationService _configurationService;
    private readonly ITranslationServiceFactory _translationFactory;
    private readonly ILogger<MachineSelectionTranslationProvider> _logger;

    public MachineSelectionTranslationProvider(
        IConfigurationService configurationService,
        ITranslationServiceFactory translationFactory,
        ILogger<MachineSelectionTranslationProvider> logger)
    {
        _configurationService = configurationService;
        _translationFactory = translationFactory;
        _logger = logger;
    }

    public async Task<SelectionTranslationResult> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default)
    {

        var provider = _configurationService.SelectionTranslation?.MachineProvider ?? Constant.MachineTranslationProviders.Baidu; 

        try
        {
            _logger.LogInformation("Using Machine Translation Provider: {Provider}", provider);

            var service = _translationFactory.CreateMachineService(provider);
            
            // Map Language Codes
            var sourceDef = LanguageService.GetLanguage(sourceLang);
            var targetDef = LanguageService.GetLanguage(targetLang);

            // Robust Resolution Logic from Manager
            var allLangs = LanguageService.GetAllLanguages().ToList();

            if (allLangs.All(l => l.Id != sourceDef.Id))
            {
                 var match = allLangs.FirstOrDefault(l => l.EnglishName.Equals(sourceLang, StringComparison.OrdinalIgnoreCase));
                 if (match != null) 
                 {
                     sourceDef = match;
                 }
                 else if (sourceLang.Equals(LanguageKeys.AutoId, StringComparison.OrdinalIgnoreCase) || sourceLang.Equals(LanguageKeys.Auto.EnglishName, StringComparison.OrdinalIgnoreCase))
                 {
                     var autoDef = allLangs.FirstOrDefault(l => l.Id.Equals(LanguageKeys.AutoId, StringComparison.OrdinalIgnoreCase));
                     if (autoDef != null) sourceDef = autoDef;
                 }
            }

            if (allLangs.All(l => l.Id != targetDef.Id))
            {
                 var match = allLangs.FirstOrDefault(l => l.EnglishName.Equals(targetLang, StringComparison.OrdinalIgnoreCase));
                 if (match != null) targetDef = match;
            }

            var resultText = await service.TranslateAsync(text, sourceDef, targetDef, false, cancellationToken);

            // Heuristic for Word vs Sentence Mode
            var isWord = !text.Trim().Contains(' ') && text.Length < 20; 
            
            if (isWord)
            {
                return new WordTranslationResult
                {
                     SourceType = TranslationSourceType.Machine,
                     Word = text,
                     Definitions = new System.Collections.Generic.List<WordDefinition>
                     {
                         new() { Pos = "", Meaning = resultText }
                     }
                };
            }

            return new SentenceTranslationResult
            {
                SourceType = TranslationSourceType.Machine,
                Origin = text,
                Translation = resultText,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Machine Translation Failed");
            throw;
        }
    }
}
