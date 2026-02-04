using System;
using System.Diagnostics;
using System.Reactive;
using System.Threading.Tasks;
using EasyChat.Services.Abstractions;
using ReactiveUI;
using SukiUI.Dialogs;

namespace EasyChat.ViewModels.Dialogs;

public class TtsPreviewInputDialogViewModel : ViewModelBase
{
    private readonly ISukiDialog _dialog;
    private readonly ITtsService _ttsService;
    private readonly IAudioPlayer _audioPlayer;
    private readonly string _voiceId;

    private string _inputText;
    public string InputText
    {
        get => _inputText;
        set => this.RaiseAndSetIfChanged(ref _inputText, value);
    }

    public bool IsPlaying
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<Unit, Unit> PlayCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    
    public Action? OnDismiss { get; set; }

    public TtsPreviewInputDialogViewModel(ISukiDialog dialog, ITtsService ttsService, IAudioPlayer audioPlayer, string voiceId)
    {
        _dialog = dialog;
        _ttsService = ttsService;
        _audioPlayer = audioPlayer;
        _voiceId = voiceId;
        
        _inputText = Lang.Resources.Tts_PreviewDefaultText;

        PlayCommand = ReactiveCommand.CreateFromTask(PlayPreview, this.WhenAnyValue(x => x.IsPlaying, x => !x));
        CloseCommand = ReactiveCommand.Create(Close);
    }

    private async Task PlayPreview()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;
        
        try 
        {
            IsPlaying = true;
            // Get audio stream
            var stream = await _ttsService.StreamAsync(InputText, _voiceId);
            
            // Enqueue to player
            if (stream != null) _audioPlayer.Enqueue(stream);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error playing preview: {ex.Message}");
        }
        finally
        {
            IsPlaying = false;
        }
    }

    private void Close()
    {
        _audioPlayer.Stop();
        OnDismiss?.Invoke();
        _dialog.Dismiss();
    }
}
