using System;
using ReactiveUI;

namespace EasyChat.Models;

public class SubtitleItem : ReactiveObject
{
    private TimeSpan _timestamp;
    public TimeSpan Timestamp
    {
        get => _timestamp;
        set
        {
            this.RaiseAndSetIfChanged(ref _timestamp, value);
            this.RaisePropertyChanged(nameof(DisplayTimestamp));
        }
    }

    private string _originalText = string.Empty;
    public string OriginalText
    {
        get => _originalText;
        set => this.RaiseAndSetIfChanged(ref _originalText, value);
    }

    private string _translatedText = string.Empty;
    public string TranslatedText
    {
        get => _translatedText;
        set => this.RaiseAndSetIfChanged(ref _translatedText, value);
    }

    public string DisplayTimestamp => Timestamp.ToString(@"hh\:mm\:ss");

    private bool _isTranslating;
    public bool IsTranslating
    {
        get => _isTranslating;
        set => this.RaiseAndSetIfChanged(ref _isTranslating, value);
    }

    private string _confirmedOriginalText = string.Empty;
    public string ConfirmedOriginalText
    {
        get => _confirmedOriginalText;
        set => this.RaiseAndSetIfChanged(ref _confirmedOriginalText, value);
    }

    private string _confirmedTranslatedText = string.Empty;
    public string ConfirmedTranslatedText
    {
        get => _confirmedTranslatedText;
        set => this.RaiseAndSetIfChanged(ref _confirmedTranslatedText, value);
    }
}
