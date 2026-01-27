using System.Collections.Generic;

namespace EasyChat.Services.Speech;

public class SpeechRecognitionConfig
{
    /// <summary>
    /// Path to the local language model (if applicable).
    /// </summary>
    public string? ModelPath { get; set; }
    
    /// <summary>
    /// List of process IDs to capture audio from.
    /// </summary>
    public IEnumerable<int>? ProcessIds { get; set; }
    
    // Future properties can be added here without breaking the interface
    // public string? ApiKey { get; set; }
    // public string? Endpoint { get; set; }
}
