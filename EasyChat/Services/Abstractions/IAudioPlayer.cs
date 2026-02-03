using System.IO;

namespace EasyChat.Services.Abstractions;

public interface IAudioPlayer
{
    /// <summary>
    /// Enqueue an audio stream (mp3) to be played.
    /// </summary>
    /// <param name="audioStream">The audio stream.</param>
    void Enqueue(Stream audioStream);

    /// <summary>
    /// Enqueue an audio file (mp3) to be played.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    void Enqueue(string filePath);

    /// <summary>
    /// Skip the current playing audio and play the next one in the queue.
    /// </summary>
    void Skip();

    /// <summary>
    /// Stop playback and clear the queue.
    /// </summary>
    void Stop();
}
