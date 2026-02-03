using System;
using System.Diagnostics;
using System.IO;
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
    private readonly string _voiceId;

    private string _inputText;
    public string InputText
    {
        get => _inputText;
        set => this.RaiseAndSetIfChanged(ref _inputText, value);
    }
    
    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set => this.RaiseAndSetIfChanged(ref _isPlaying, value);
    }

    public ReactiveCommand<Unit, Unit> PlayCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    
    public Action? OnDismiss { get; set; }

    public TtsPreviewInputDialogViewModel(ISukiDialog dialog, ITtsService ttsService, string voiceId)
    {
        _dialog = dialog;
        _ttsService = ttsService;
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
            // Create temp file
            var tempPath = Path.Combine(Path.GetTempPath(), $"easychat_preview_{Guid.NewGuid()}.mp3");
            
            await _ttsService.SynthesizeAsync(InputText, _voiceId, tempPath);
            
            if (File.Exists(tempPath))
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo(tempPath)
                {
                    UseShellExecute = true
                };
                process.Start();
            }
        }
        catch (Exception ex)
        {
            // Should probably log or show toast, but for simple preview silent fail or debug log is okay for now
            // Or maybe inject ToastManager if we want robustness
            Debug.WriteLine($"Error playing preview: {ex.Message}");
        }
        finally
        {
            IsPlaying = false;
        }
    }

    private void Close()
    {
        OnDismiss?.Invoke();
        _dialog.Dismiss();
    }
}
