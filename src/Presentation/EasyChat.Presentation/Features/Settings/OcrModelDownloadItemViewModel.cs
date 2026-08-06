using EasyChat.Presentation.Lang;
using EasyChat.Contracts.Ocr;
using ReactiveUI;

namespace EasyChat.Presentation.Features.Settings;

public sealed class OcrModelDownloadItemViewModel : ReactiveObject
{
    private bool _isDownloaded;
    private bool _isDownloading;
    private bool _isFailed;
    private double _progress;
    private string? _errorMessage;
    public OcrModelDownloadItemViewModel(
        OcrModelPackage package,
        string displayName,
        string description,
        string supportedLanguages,
        bool isDownloaded)
    {
        Package = package ?? throw new ArgumentNullException(nameof(package));
        DisplayName = displayName;
        Description = description;
        SupportedLanguages = supportedLanguages;
        IsSupportedLanguageListCompact = package.SupportedLanguages.Count >= 20;
        SupportedLanguagesSummary = IsSupportedLanguageListCompact
            ? string.Format(Resources.OcrSupportedLanguageCount, package.SupportedLanguages.Count)
            : supportedLanguages;
        _isDownloaded = isDownloaded;
        _progress = isDownloaded ? 1 : 0;
    }

    public OcrModelPackage Package { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string SupportedLanguages { get; }
    public string SupportedLanguagesSummary { get; }
    public bool IsSupportedLanguageListCompact { get; }
    public bool IsSupportedLanguageListInline => !IsSupportedLanguageListCompact;

    public bool IsDownloaded
    {
        get => _isDownloaded;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isDownloaded, value);
            this.RaisePropertyChanged(nameof(IsActionVisible));
            this.RaisePropertyChanged(nameof(IsDeleteVisible));
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isDownloading, value);
            this.RaisePropertyChanged(nameof(IsActionVisible));
            this.RaisePropertyChanged(nameof(IsCancelVisible));
            this.RaisePropertyChanged(nameof(IsDeleteVisible));
            this.RaisePropertyChanged(nameof(IsProgressIndeterminate));
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    public bool IsFailed
    {
        get => _isFailed;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isFailed, value);
            this.RaisePropertyChanged(nameof(ActionText));
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    public double Progress
    {
        get => _progress;
        private set
        {
            this.RaiseAndSetIfChanged(ref _progress, value);
            this.RaisePropertyChanged(nameof(ProgressText));
            this.RaisePropertyChanged(nameof(IsProgressIndeterminate));
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public bool IsActionVisible => !IsDownloading && !IsDownloaded;
    public bool IsCancelVisible => IsDownloading;
    public bool IsDeleteVisible => IsDownloaded && !IsDownloading;
    public bool IsProgressVisible => true;
    public bool IsProgressIndeterminate => IsDownloading && Progress <= 0;
    public string ProgressText => IsDownloading && Progress > 0 ? $"{Progress:P0}" : string.Empty;
    public string ActionText => IsFailed ? Resources.RetryOcrModel : Resources.DownloadOcrModel;

    public string StatusText
    {
        get
        {
            if (IsDownloaded) return Resources.OcrModelDownloaded;
            if (IsDownloading) return Resources.OcrModelDownloading;
            if (IsFailed) return Resources.OcrModelDownloadFailed;
            return Resources.OcrModelNotDownloaded;
        }
    }

    public void StartDownload()
    {
        IsFailed = false;
        ErrorMessage = null;
        Progress = 0;
        IsDownloading = true;
    }

    public void SetProgress(double value) => Progress = value;

    public void CompleteDownload()
    {
        Progress = 1;
        IsDownloaded = true;
        IsDownloading = false;
        IsFailed = false;
    }

    public void CancelDownload()
    {
        IsDownloading = false;
        Progress = 0;
    }

    public void MarkDeleted()
    {
        IsDownloaded = false;
        IsFailed = false;
        ErrorMessage = null;
        Progress = 0;
    }

    public void FailDownload(string message)
    {
        IsDownloading = false;
        IsFailed = true;
        ErrorMessage = message;
    }
}
