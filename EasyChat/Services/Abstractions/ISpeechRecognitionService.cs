using System;
using System.Threading.Tasks;
using EasyChat.Services.Speech.Asr;

namespace EasyChat.Services.Abstractions;

public interface ISpeechRecognitionService
{
    bool IsRecording { get; }
    bool IsBusy { get; }
    

    
    Task InitializeAsync();
    
    /// <summary>
    /// Starts recording with the specified configuration.
    /// </summary>
    /// <param name="config">The configuration object containing parameters like model path or capture targets.</param>
    Task StartRecordingAsync(SpeechRecognitionConfig config);
    
    Task StopRecordingAsync();
    

    
    event Action<string> OnFinalResult;
    event Action<string> OnPartialResult;
    event Action<string> OnError;
    event Action OnStarted;
    event Action OnStopped;
}
