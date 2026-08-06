using EasyChat.Contracts.Input;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Contracts.Translation;
using EasyChat.Shared.Results;

namespace EasyChat.Application.Input;

public sealed class InputTranslationUseCases(
    ISettingsUseCases settings,
    ITranslationLanguageCatalog languages,
    ITranslationUseCases translation,
    IInputDeliveryUseCases delivery) : IInputTranslationUseCases
{
    private readonly ISettingsUseCases _settings = settings;
    private readonly ITranslationLanguageCatalog _languages = languages;
    private readonly ITranslationUseCases _translation = translation;
    private readonly IInputDeliveryUseCases _delivery = delivery;

    public async ValueTask<Result> TranslateAndDeliverAsync(
        InputTranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var input = _settings.Current.Input;
        var general = _settings.Current.General;
        var sourceId = request.SourceLanguageId ?? general.SourceLanguage.Id;
        var targetId = request.TargetLanguageId ?? general.TargetLanguage.Id;
        if (input.ReverseTranslateLanguage)
            (sourceId, targetId) = (targetId, sourceId);

        var translated = await _translation.TranslateAsync(
            new TranslationRequest(
                request.Text,
                _languages.Get(sourceId),
                _languages.Get(targetId),
                PlainText: true),
            cancellationToken).ConfigureAwait(false);
        if (translated.IsFailure)
            return Result.Failure(translated.Error);
        if (string.IsNullOrWhiteSpace(translated.Value.Text))
            return Result.Failure(new Error("input.translation-empty", "Translation returned no text."));

        return await _delivery.DeliverAsync(
            new InputDeliveryRequest(
                translated.Value.Text,
                request.Target,
                input.DeliveryMode switch
                {
                    InputDeliveryMode.Paste => TextDeliveryMode.Paste,
                    InputDeliveryMode.Message => TextDeliveryMode.Message,
                    _ => TextDeliveryMode.Type
                },
                TimeSpan.FromMilliseconds(Math.Max(0, input.KeySendDelay)),
                request.ReplaceCurrentInput,
                request.BeforeKey,
                request.AfterKey),
            cancellationToken).ConfigureAwait(false);
    }
}
