using EasyChat.Contracts.ApplicationData;
using EasyChat.Contracts.Speech;
using MicroASR;

namespace EasyChat.Infrastructure.Speech.Recognition;

public sealed class MicroAsrSpeechRecognitionModelCatalog : ISpeechRecognitionModelCatalog
{
    private readonly Func<string> _modelsDirectory;

    public MicroAsrSpeechRecognitionModelCatalog(IApplicationDataPaths applicationData)
        : this(() => applicationData.SpeechModelsDirectory)
    {
        ArgumentNullException.ThrowIfNull(applicationData);
    }

    internal MicroAsrSpeechRecognitionModelCatalog(string modelsDirectory)
        : this(() => modelsDirectory)
    {
    }

    private MicroAsrSpeechRecognitionModelCatalog(Func<string> modelsDirectory)
    {
        _modelsDirectory = modelsDirectory;
    }

    public event EventHandler? ModelsChanged;

    internal string ModelsDirectory => Path.GetFullPath(_modelsDirectory());

    internal void NotifyModelsChanged() => ModelsChanged?.Invoke(this, EventArgs.Empty);

    public async ValueTask<IReadOnlyList<SpeechRecognitionModel>> GetModelsAsync(
        CancellationToken cancellationToken = default) =>
        await Task.Run(() => Discover(cancellationToken), cancellationToken).ConfigureAwait(false);

    private IReadOnlyList<SpeechRecognitionModel> Discover(CancellationToken cancellationToken)
    {
        var modelsDirectory = ModelsDirectory;
        if (!Directory.Exists(modelsDirectory))
            return [];

        var models = new List<SpeechRecognitionModel>();
        foreach (var directory in Directory.EnumerateDirectories(modelsDirectory)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (SpeechModelPackage.IsSupported(directory))
                models.Add(new SpeechRecognitionModel(Path.GetFileName(directory)));
        }
        return models;
    }
}
