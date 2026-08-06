using EasyChat.Shared.Results;

namespace EasyChat.Contracts.ApplicationData;

public sealed record ApplicationDataLocation(string RootDirectory, bool IsDefault);

public sealed class ApplicationDataLocationChangedEventArgs(
    ApplicationDataLocation previous,
    ApplicationDataLocation current) : EventArgs
{
    public ApplicationDataLocation Previous { get; } = previous;
    public ApplicationDataLocation Current { get; } = current;
}

public interface IApplicationDataPaths
{
    event EventHandler<ApplicationDataLocationChangedEventArgs>? LocationChanged;

    ApplicationDataLocation Current { get; }
    string ConfigurationDirectory { get; }
    string SpeechModelsDirectory { get; }
    string OcrModelsDirectory { get; }
}

public interface IApplicationDataStore : IApplicationDataPaths
{
    ValueTask<Result<ApplicationDataLocation>> ChangeLocationAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default);
}

public interface IApplicationDataUseCases
{
    ApplicationDataLocation Current { get; }

    ValueTask<Result<ApplicationDataLocation>> ChangeLocationAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default);
}
