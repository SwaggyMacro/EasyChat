using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using EasyChat.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace EasyChat.Services.Speech.Asr;

public class SpeechRecognitionService : ISpeechRecognitionService, IDisposable
{
    private readonly ILogger<SpeechRecognitionService> _logger;
    private AsrNativeInterop.RecognitionCallback _callbackDelegate;
    
    // Dedicated Worker Thread for ASR (STA)
    private readonly BlockingCollection<Action> _workerQueue = new();
    private readonly Thread? _asrWorkerThread;
    private readonly CancellationTokenSource _cts = new();

    public bool IsRecording
    {
        get;
        private set
        {
            if (!field.Equals(value))
            {
                field = value;
            }
        }
    }

    private bool _isBusy;
    public bool IsBusy 
    {
        get => _isBusy;
        private set => _isBusy = value;
    }



    public event Action<string>? OnFinalResult;
    public event Action<string>? OnPartialResult;
    public event Action<string>? OnError;
    public event Action? OnStarted;
    public event Action? OnStopped;

    public SpeechRecognitionService(ILogger<SpeechRecognitionService> logger)
    {
        _logger = logger;
        _callbackDelegate = OnRecognitionResult;

        if (OperatingSystem.IsWindows())
        {
            _asrWorkerThread = new Thread(WorkerLoop) { IsBackground = true, Name = "ASR Worker" };
            _asrWorkerThread.SetApartmentState(ApartmentState.STA);
            _asrWorkerThread.Start();
        }
    }

    public Task InitializeAsync()
    {
        // Initialization is done implicitly when starting, or could be explicit. 
        // For now, mirroring existing logic which inits on start.
        return Task.CompletedTask;
    }

    public Task StartRecordingAsync(SpeechRecognitionConfig config)
    {
        if (IsBusy) return Task.CompletedTask;
        if (!OperatingSystem.IsWindows()) return Task.CompletedTask;

        IsBusy = true;
        // Clone config to ensure thread safety if modified externally? 
        // For now take what's needed.
        var modelName = config.ModelPath ?? "";
        var pids = config.ProcessIds?.ToList() ?? new List<int>();

        _workerQueue.Add(() =>
        {
            try
            {
                string libPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lib");
                string modelPath = Path.Combine(libPath, modelName);

                if (AsrNativeInterop.Initialize(modelPath))
                {
                    AsrNativeInterop.SetCallback(_callbackDelegate);
                    
                    int[] targetPids;
                    if (pids.Count == 0 || pids.Contains(0))
                    {
                        targetPids = [0];
                    }
                    else
                    {
                        targetPids = pids.ToArray();
                    }

                    AsrNativeInterop.StartLoopbackCapture(targetPids, targetPids.Length);
                    AsrNativeInterop.StartRecognition();
                    
                    Dispatcher.UIThread.Post(() => 
                    {
                        IsRecording = true;
                        OnStarted?.Invoke();
                    });
                }
                else
                {
                    _logger.LogError("ASR Initialization failed.");
                    OnError?.Invoke("Initialization failed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting ASR");
                OnError?.Invoke(ex.Message);
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsBusy = false);
            }
        });

        return Task.CompletedTask;
    }

    public Task StopRecordingAsync()
    {
        if (IsBusy || !IsRecording) return Task.CompletedTask;
        if (!OperatingSystem.IsWindows()) return Task.CompletedTask;
        
        IsBusy = true;

        _workerQueue.Add(() =>
        {
            try
            {
                AsrNativeInterop.Cleanup(); // This stops capture and recognition
                Dispatcher.UIThread.Post(() => 
                { 
                    IsRecording = false; 
                    OnStopped?.Invoke();
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping ASR");
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsBusy = false);
            }
        });
        
        return Task.CompletedTask;
    }


    private void WorkerLoop()
    {
        try
        {
            foreach (var action in _workerQueue.GetConsumingEnumerable(_cts.Token))
            {
                try
                {
                    action();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ASR Worker Error");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown, avoid exception on exit.
        }
    }

    private void OnRecognitionResult(int type, string result)
    {
        // Marshal back to some context if needed, but events are usually fine on any thread, 
        // however consumers (ViewModel) often expect UI thread updates. 
        // I will invoke events on the original thread (likely worker thread) 
        // and let ViewModel dispatch to UI thread.
        // ACTUALLY, the original code performed UI updates in the callback.
        
        switch (type)
        {
            case 0: // Final
                OnFinalResult?.Invoke(result);
                break;
            case 1: // Partial
                OnPartialResult?.Invoke(result);
                break;
            case 2: // Error
                _logger.LogError("ASR Error: {Result}", result);
                OnError?.Invoke(result);
                break;
            case 3: // Canceled
                Dispatcher.UIThread.Post(() => IsRecording = false);
                OnStopped?.Invoke();
                break;
        }
    }


    public void Dispose()
    {
        _cts.Cancel();
        _workerQueue.CompleteAdding();
        if (_asrWorkerThread is { IsAlive: true })
        {
            // _asrWorkerThread.Join(1000); 
        }
    }
}
