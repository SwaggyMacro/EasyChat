using System;
using ReactiveUI;

namespace EasyChat.Models;

public class SubtitleItem : ReactiveObject
{
    public TimeSpan Timestamp
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(DisplayTimestamp));
        }
    }

    private string _originalText = string.Empty;
    public string OriginalText
    {
        get => _originalText;
        set 
        {
            this.RaiseAndSetIfChanged(ref _originalText, value);
            this.RaisePropertyChanged(nameof(DisplayTranslatedText));
        }
    }

    public string TranslatedText
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public string DisplayTimestamp => Timestamp.ToString(@"hh\:mm\:ss");

    public bool IsTranslating
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(DisplayTranslatedText));
        }
    }

    public string ConfirmedOriginalText
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public string ConfirmedTranslatedText
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    // DisplayTranslatedText is used for smooth UI display - it preserves the previous
    // translation during retranslation to prevent flickering/jumping
    public string DisplayTranslatedText
    {
        get
        {
            if (string.IsNullOrEmpty(field))
            {
                // Show placeholder if we have actual text content (not initial "...")
                // This ensures immediate feedback as soon as detection starts, 
                // and persistent feedback even if the translation network request momentarily lapses.
                if (IsTranslating && !string.IsNullOrWhiteSpace(_originalText) && _originalText != "..." && _originalText != "…")
                {
                    return Lang.Resources.Speech_Translating;
                }
            }
            return field;
        }
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;
}
