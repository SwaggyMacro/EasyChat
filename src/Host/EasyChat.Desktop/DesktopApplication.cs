using System.Globalization;
using System.Diagnostics;
using Avalonia;
using EasyChat.Application.DependencyInjection;
using EasyChat.Contracts.Shell;
using EasyChat.Contracts.Input;
using EasyChat.Contracts.Translation;
using EasyChat.Contracts.Updates;
using EasyChat.Infrastructure.DependencyInjection;
using EasyChat.Presentation.Lang;
using EasyChat.Presentation.DependencyInjection;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Features.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace EasyChat.Desktop;

public static class DesktopApplication
{
    private const string VerifyCompositionArgument = "--verify-composition";
    private const string RestartArgument = "--restart";

    public static void Run(
        string[] args,
        Action<IServiceCollection> addPlatformServices,
        Action? initializeDeployment = null,
        Action<AppBuilder>? configureAppBuilder = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(addPlatformServices);

        if (args.Length == 1 && string.Equals(
                args[0],
                VerifyCompositionArgument,
                StringComparison.Ordinal))
        {
            using var verificationServices = BuildServices(addPlatformServices);
            return;
        }

        var isRestart = args.Any(argument =>
            string.Equals(argument, RestartArgument, StringComparison.Ordinal));
        using var singleInstance = DesktopSingleInstance.AcquireOrSignal(isRestart);
        if (singleInstance is null)
            return;

        var services = BuildServices(addPlatformServices);
        IShellLifecycle? shell = null;
        DesktopUiContext? ui = null;
        try
        {
            initializeDeployment?.Invoke();
            StartTsf(services);
            shell = StartShell(services);
            InitializeSettings(services);
            var startInTray = DesktopStartupBehavior.ShouldStartInTray(
                args,
                services.GetRequiredService<SettingsSession>().General.ClosingBehavior);
            var builder = AppBuilder.Configure(() => new App(
                    () => ui ??= CreateUiContext(services),
                    singleInstance.SetActivationHandler,
                    startInTray))
                .UsePlatformDetect();
            configureAppBuilder?.Invoke(builder);
            builder.WithInterFont()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Shutdown(services, shell, ui?.Interactions);
        }
    }

    private static ServiceProvider BuildServices(Action<IServiceCollection> addPlatformServices)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(AppContext.BaseDirectory, "Logs", "log_.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddSerilog(dispose: true);
            builder.AddConsole();
            builder.AddDebug();
        });
        services.AddEasyChatInfrastructure();
        addPlatformServices(services);
        services.AddEasyChatApplication(new TranslationMessages(Resources.RequestError));
        services.AddEasyChatPresentation();
        services.AddSingleton<IApplicationRestartService, DesktopApplicationRestartService>();
        services.AddSingleton<DesktopInteractionLifecycle>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static DesktopUiContext CreateUiContext(IServiceProvider services)
    {
        var mainWindowViewModel = services.GetRequiredService<MainWindowViewModel>();
        return new DesktopUiContext(
            services.GetRequiredService<SettingsSession>(),
            mainWindowViewModel,
            services.GetRequiredService<DesktopInteractionLifecycle>(),
            services.GetRequiredService<IApplicationUpdateService>(),
            mainWindowViewModel.UpdateToastManager,
            services.GetRequiredService<EasyChat.Presentation.Features.Capture.IScreenshotCaptureSession>(),
            services.GetRequiredService<EasyChat.Presentation.Features.Input.TsfCandidateWindowCoordinator>());
    }

    private static IShellLifecycle StartShell(IServiceProvider services)
    {
        var shell = services.GetRequiredService<IShellLifecycle>();
        var started = shell.StartAsync().AsTask().GetAwaiter().GetResult();
        if (started.IsFailure)
            throw new InvalidOperationException(started.Error.Message);
        return shell;
    }

    private static void InitializeSettings(IServiceProvider services)
    {
        var settings = services.GetRequiredService<SettingsSession>();
        var attached = settings.AttachCurrent();
        if (attached.IsFailure)
            throw new InvalidOperationException(attached.Error.Message);

        var culture = string.Equals(
            settings.General.DisplayLanguage,
            "Simplified Chinese",
            StringComparison.Ordinal)
            ? new CultureInfo("zh-CN")
            : new CultureInfo("en-US");
        Resources.Culture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    private static void StartTsf(IServiceProvider services)
    {
        var tsf = services.GetRequiredService<ITsfInputTranslationUseCases>();
        var started = tsf.StartAsync().AsTask().GetAwaiter().GetResult();
        if (started.IsFailure)
            Log.Warning("TSF input translation is unavailable: {Message}", started.Error.Message);
    }

    private static void Shutdown(
        ServiceProvider services,
        IShellLifecycle? shell,
        DesktopInteractionLifecycle? interactions)
    {
        try
        {
            if (shell is not null)
            {
                interactions?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                shell.StopAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        finally
        {
            services.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}

internal sealed class DesktopApplicationRestartService : IApplicationRestartService
{
    public void Restart()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
            startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add("--restart");

        if (Process.Start(startInfo) is null)
            return;

        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
