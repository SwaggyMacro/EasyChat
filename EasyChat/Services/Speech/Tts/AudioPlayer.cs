using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EasyChat.Services.Abstractions;
using Serilog;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace EasyChat.Services.Speech.Tts;

public class AudioPlayer : IAudioPlayer, IDisposable
{
    private readonly ConcurrentQueue<Func<Task>> _playbackQueue = new();
    private readonly object _lockObj = new();
    private volatile bool _isPlaying;
    private bool _stopRequested;

    // SoundFlow components
    private AudioEngine? _engine;
    private AudioPlaybackDevice? _playbackDevice;

    public AudioPlayer()
    {
        InitializeSoundFlow();
    }

    private void InitializeSoundFlow()
    {
        try
        {
            // 1. Initialize engine
            _engine = new MiniAudioEngine();
            
            // 2. Define format (Example used DvdHq)
            var format = AudioFormat.DvdHq;
            
            // 3. Initialize device
            _engine.UpdateAudioDevicesInfo();
            var defaultDevice = _engine.PlaybackDevices.FirstOrDefault(x => x.IsDefault);
            _playbackDevice = _engine.InitializePlaybackDevice(defaultDevice, format);
            
            // 6. Start the device (Important from example)
            _playbackDevice.Start();
            
            Log.Information("SoundFlow AudioPlayer initialized.");
        }
        catch (Exception ex)
        {
             Log.Error(ex, "Exception during SoundFlow initialization");
        }
    }

    public void Enqueue(Stream audioStream)
    {
        byte[] buffer;
        try
        {
            if (audioStream is MemoryStream ms)
            {
                buffer = ms.ToArray();
            }
            else
            {
                using var tempMs = new MemoryStream();
                audioStream.CopyTo(tempMs);
                buffer = tempMs.ToArray();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read audio stream for queue");
            return;
        }

        _playbackQueue.Enqueue(async () => await PlayFromMemory(buffer));
        CheckPlayNext();
    }

    public void Enqueue(string filePath)
    {
        _playbackQueue.Enqueue(async () => await PlayFromFile(filePath));
        CheckPlayNext();
    }

    private async void CheckPlayNext()
    {
        lock (_lockObj)
        {
            if (_isPlaying) return;
            if (_playbackQueue.IsEmpty) return;
            
            _isPlaying = true;
            _stopRequested = false;
        }

        if (_playbackQueue.TryDequeue(out var action))
        {
             try
             {
                 await action();
             }
             catch (Exception ex)
             {
                 Log.Error(ex, "Error playing audio item");
             }
             finally
             {
                 lock(_lockObj) 
                 { 
                     _isPlaying = false;
                 }
                 CheckPlayNext();
             }
        }
    }

    private async Task PlayFromMemory(byte[] data)
    {
        if (_engine == null || _playbackDevice == null) return;
        
        var ms = new MemoryStream(data);
        SoundPlayer? player = null;
        StreamDataProvider? dataProvider = null;

        try
        {
             // Use AudioFormat from device (initialized with DvdHq ideally)
             var format = _playbackDevice.Format;
             
             // 4. Create SoundPlayer with DataProvider (User Example Pattern)
             // Assumption: StreamDataProvider exists in SoundFlow.Providers
             dataProvider = new StreamDataProvider(_engine, format, ms);
             player = new SoundPlayer(_engine, format, dataProvider);

             lock(_lockObj)
             {
                if (_stopRequested) 
                {
                    player.Dispose();
                    dataProvider.Dispose();
                    return;
                }
             }
             
             // 5. Add to mixer
             _playbackDevice.MasterMixer.AddComponent(player);
             
             // 7. Start player
             player.Play();

             // Poll for completion
             while (player.State == PlaybackState.Playing && !_stopRequested)
             {
                 await Task.Delay(50);
             }
             
             player.Stop();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error playing from memory");
        }
        finally
        {
            if (player != null)
            {
                try { _playbackDevice.MasterMixer.RemoveComponent(player); }
                catch
                {
                    // ignored
                }

                player.Dispose();
            }
            dataProvider?.Dispose();
            await ms.DisposeAsync(); 
        }
    }

    private async Task PlayFromFile(string filePath)
    {
        if (_engine == null || _playbackDevice == null) return;

        SoundPlayer? player = null;
        StreamDataProvider? dataProvider = null;
        FileStream? fs = null;

        try
        {
            var format = _playbackDevice.Format;
            
            fs = File.OpenRead(filePath);
            dataProvider = new StreamDataProvider(_engine, format, fs);
            player = new SoundPlayer(_engine, format, dataProvider);

             lock(_lockObj)
            {
                if (_stopRequested) 
                {
                    player.Dispose();
                    dataProvider.Dispose();
                    return;
                }
            }

            _playbackDevice.MasterMixer.AddComponent(player);
            player.Play();

            while (player.State == PlaybackState.Playing && !_stopRequested)
            {
                await Task.Delay(50);
            }
            player.Stop();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error playing from file: {FilePath}", filePath);
        }
        finally
        {
            if (player != null)
            {
                 try { _playbackDevice.MasterMixer.RemoveComponent(player); }
                 catch
                 {
                     // ignored
                 }

                 player.Dispose();
            }
            dataProvider?.Dispose();
            if(fs != null) await fs.DisposeAsync();
        }
    }

    public void Skip()
    {
        lock(_lockObj)
        {
            _stopRequested = true;
        }
    }

    public void Stop()
    {
        lock (_lockObj)
        {
            _stopRequested = true;
            while (!_playbackQueue.IsEmpty) _playbackQueue.TryDequeue(out _);
        }
    }

    public void Dispose()
    {
        Stop();
        if (_playbackDevice != null)
        {
            _playbackDevice.Stop();
            _playbackDevice.Dispose();
        }
        if (_engine != null)
        {
            _engine.Dispose();
        }
    }
}
