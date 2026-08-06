using EasyChat.Contracts.ApplicationData;
using EasyChat.Contracts.Settings;
using EasyChat.Shared.Results;

namespace EasyChat.Application.ApplicationData;

public sealed class ApplicationDataUseCases(
    IApplicationDataStore store,
    ISettingsUseCases settings) : IApplicationDataUseCases
{
    private readonly IApplicationDataStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ISettingsUseCases _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public ApplicationDataLocation Current => _store.Current;

    public async ValueTask<Result<ApplicationDataLocation>> ChangeLocationAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        var flush = await _settings.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (flush.IsFailure)
            return Result<ApplicationDataLocation>.Failure(flush.Error);

        return await _store.ChangeLocationAsync(rootDirectory, cancellationToken).ConfigureAwait(false);
    }
}
