using EasyChat.Models.Configuration;

namespace EasyChat.Services.Abstractions;

/// <summary>
/// Interface for handling shortcut key actions.
/// Each handler is responsible for executing a specific type of shortcut action.
/// </summary>
public interface IShortcutActionHandler
{
    /// <summary>
    /// The action type this handler responds to.
    /// Must match the ActionType in ShortcutEntry configuration.
    /// </summary>
    string ActionType { get; }

    /// <summary>
    /// If true, prevents re-execution while a previous invocation is still running.
    /// Default: false (allow concurrent execution).
    /// </summary>
    bool PreventConcurrentExecution => false;

    /// <summary>
    /// Returns true if the handler is currently executing an operation.
    /// Only relevant when PreventConcurrentExecution is true.
    /// </summary>
    bool IsExecuting => false;

    /// <summary>
    /// Execute the shortcut action.
    /// </summary>
    /// <param name="parameter">Optional parameter for the action.</param>
    void Execute(ShortcutParameter? parameter = null);
}
