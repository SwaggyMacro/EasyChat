using System;
using System.Collections.Generic;
using System.Linq;
using EasyChat.Services.Abstractions;
using EasyChat.Services.Shortcuts;

namespace EasyChat.Services;

/// <summary>
/// Service for managing global keyboard shortcuts.
/// Registers/unregisters hotkeys based on configuration and dispatches actions to handlers.
/// </summary>
public class GlobalShortcutService : IDisposable
{
    private readonly IHotKeyManager _hotKeyManager;
    private readonly IConfigurationService _configurationService;
    private readonly Dictionary<string, IShortcutActionHandler> _handlers;
    private readonly List<IDisposable> _activeHotKeys = new();

    public GlobalShortcutService(
        IHotKeyManager hotKeyManager,
        IConfigurationService configurationService,
        IEnumerable<IShortcutActionHandler> handlers)
    {
        _hotKeyManager = hotKeyManager;
        _configurationService = configurationService;
        _handlers = handlers.ToDictionary(h => h.ActionType, h => h);

        // Subscribe to configuration changes
        if (_configurationService.Shortcut?.Entries != null)
        {
            _configurationService.Shortcut.Entries.CollectionChanged += OnShortcutEntriesChanged;
        }

        // Initial Registration
        RegisterHotKeys();
    }

    private void OnShortcutEntriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RegisterHotKeys();
    }

    private void RegisterHotKeys()
    {
        // Dispose existing hotkeys
        foreach (var hotKey in _activeHotKeys)
        {
            hotKey.Dispose();
        }
        _activeHotKeys.Clear();

        if (_configurationService.Shortcut?.Entries == null) return;

        foreach (var entry in _configurationService.Shortcut.Entries)
        {
            if (!entry.IsEnabled || string.IsNullOrWhiteSpace(entry.KeyCombination)) continue;

            var parsed = KeyCombinationParser.Parse(entry.KeyCombination);
            if (!parsed.HasValue) continue;

            // Find handler for this action type
            if (!_handlers.TryGetValue(entry.ActionType, out var handler)) continue;

            // Capture parameter for closure
            var parameter = entry.Parameter;

            var hotKey = _hotKeyManager.Register(
                parsed.Value.modifiers,
                parsed.Value.key,
                () =>
                {
                    // Debounce: skip if handler is already executing
                    if (handler.PreventConcurrentExecution && handler.IsExecuting)
                        return;
                    handler.Execute(parameter);
                });

            if (hotKey != null)
            {
                _activeHotKeys.Add(hotKey);
            }
        }
    }

    public void Dispose()
    {
        foreach (var hotKey in _activeHotKeys)
        {
            hotKey.Dispose();
        }
        _activeHotKeys.Clear();

        if (_configurationService.Shortcut?.Entries != null)
        {
            _configurationService.Shortcut.Entries.CollectionChanged -= OnShortcutEntriesChanged;
        }
    }
}
