using Avalonia.Controls;
using Avalonia.Media;
using EasyChat.Contracts.Input;
using EasyChat.Presentation.Lang;
using ReactiveUI;

namespace EasyChat.Presentation.Features.Input.Views;

public partial class TsfCandidateWindowView : Window
{
    private readonly TsfCandidateWindowState _state = new();

    public TsfCandidateWindowView()
    {
        InitializeComponent();
        DataContext = _state;
    }

    public void Update(TsfCandidateChanged candidate)
    {
        _state.SourceText = candidate.SourceText;
        _state.TranslationText = string.IsNullOrWhiteSpace(candidate.TranslationText)
            ? candidate.SourceText
            : candidate.TranslationText;
        _state.StatusText = candidate.Status switch
        {
            TsfCandidateStatus.Translating => global::EasyChat.Presentation.Lang.Resources.TsfCandidate_Translating,
            TsfCandidateStatus.Preview => global::EasyChat.Presentation.Lang.Resources.TsfCandidate_Preview,
            TsfCandidateStatus.Committed => global::EasyChat.Presentation.Lang.Resources.TsfCandidate_Committed,
            TsfCandidateStatus.Failed => candidate.ErrorMessage ?? global::EasyChat.Presentation.Lang.Resources.TsfCandidate_Failed,
            TsfCandidateStatus.Unsupported => global::EasyChat.Presentation.Lang.Resources.TsfCandidate_Unsupported,
            _ => string.Empty
        };
        _state.StatusBrush = candidate.Status == TsfCandidateStatus.Failed
            ? Brushes.OrangeRed
            : Brushes.Gray;
    }
}

public sealed class TsfCandidateWindowState : ReactiveObject
{
    private string _sourceText = string.Empty;
    private string _translationText = string.Empty;
    private string _statusText = string.Empty;
    private IBrush _statusBrush = Brushes.Gray;

    public string SourceText { get => _sourceText; set => this.RaiseAndSetIfChanged(ref _sourceText, value); }
    public string TranslationText { get => _translationText; set => this.RaiseAndSetIfChanged(ref _translationText, value); }
    public string StatusText { get => _statusText; set => this.RaiseAndSetIfChanged(ref _statusText, value); }
    public IBrush StatusBrush { get => _statusBrush; set => this.RaiseAndSetIfChanged(ref _statusBrush, value); }
}
