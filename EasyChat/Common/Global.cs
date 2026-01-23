using System;
using Avalonia.Controls;
using EasyChat.Models;
using EasyChat.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using SukiUI.Toasts;
// Added for Screens

namespace EasyChat;

public static class Global
{
    public static Config Config { get; set; } = new();

    // Screens accessor for services
    public static Screens? Screens { get; set; }

    // ServiceProvider is set in App.axaml.cs during startup
    public static IServiceProvider? Services { get; set; }

    public static ISukiToastManager ToastManager => Services?.GetRequiredService<ISukiToastManager>()
                                                    ?? throw new InvalidOperationException(
                                                        "ToastManager not initialized");

    public static IHotKeyManager HotKeyManager => Services?.GetRequiredService<IHotKeyManager>()
                                                  ?? throw new InvalidOperationException(
                                                      "HotKeyManager not initialized");
}