using System.Collections.Generic;

namespace EasyChat.Constants;

public static class ModelList
{
    public static readonly List<string> OpenAiModels =
    [
        "gpt-3.5-turbo",
        "gpt-3.5-turbo-16k",
        "text-embedding-ada-002",
        "text-embedding-3-large",
        "text-embedding-3-small",
        "gpt-3.5-turbo-instruct",
        "o1-mini",
        "o1-preview",
        "chatgpt-4o-latest",
        "gpt-4o-mini",
        "gpt-4o",
        "gpt-4o-2024-05-13",
        "gpt-4o-2024-08-06",
        "gpt-4-turbo",
        "gpt-4-0125-preview",
        "gpt-4-1106-preview",
        "gpt-4-turbo-preview"
    ];

    public static readonly List<string> GeminiModels =
    [
        "gemini-1.5-flash-8b",
        "gemini-1.5-pro-002",
        "gemini-1.5-pro-001",
        "gemini-1.5-flash-002",
        "gemini-1.5-flash-exp-0827",
        "gemini-1.5-pro-exp-0801",
        "gemini-1.5-pro-exp-0827",
        "gemini-1.5-pro-latest",
        "gemini-1.5-flash-latest",
        "gemini-pro",
        "gemini-pro-vision"
    ];

    public static readonly List<string> ClaudeModels =
    [
        "claude-3-5-sonnet-20241022",
        "claude-3-5-sonnet-20240620",
        "claude-3-haiku-20240307",
        "claude-3-sonnet-20240229",
        "claude-3-opus-20240229"
    ];
}