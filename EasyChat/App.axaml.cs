using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EasyChat.Common;
using EasyChat.Models;
using EasyChat.Models.Configuration;
using EasyChat.Services;
using EasyChat.Services.Abstractions;
using EasyChat.Services.HotKey;
using EasyChat.Services.Ocr;
using EasyChat.Services.Platform;
using EasyChat.Services.Languages;
using EasyChat.Services.Languages.Providers;
using EasyChat.Services.Shortcuts;
using EasyChat.Services.Shortcuts.Handlers;
using EasyChat.Mappers;
using EasyChat.ViewModels;
using EasyChat.ViewModels.Pages;
using EasyChat.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace EasyChat;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Force rebuild

        // Load Language Setting
        try
        {
            var generalConf = ConfigUtil.LoadConfig<General>("General");
            if (!string.IsNullOrEmpty(generalConf.Language))
            {
                var culture = generalConf.Language switch
                {
                    "Simplified Chinese" => new CultureInfo("zh-CN"),
                    _ => new CultureInfo("en-US")
                };
                Lang.Resources.Culture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                Thread.CurrentThread.CurrentCulture = culture;
            }
        }
        catch (Exception)
        {
            // Ignore config load error on startup
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "log_.txt"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            // Service Collection
            var services = new ServiceCollection();

            // Logging
            services.AddLogging(builder => builder.AddSerilog(dispose: true));

            // SukiUI Managers
            services.AddSingleton<ISukiToastManager, SukiToastManager>();
            services.AddSingleton<ISukiDialogManager, SukiDialogManager>();

            // Configuration Service (Initialize early)
            services.AddSingleton<IConfigurationService, ConfigurationService>();

            // Platform & Core Services
            if (OperatingSystem.IsWindows())
            {
                services.AddSingleton<IPlatformService, WindowsPlatformService>();
                services.AddSingleton<IHotKeyManager, WindowsHotKeyManager>();
                services.AddSingleton<IScreenCaptureService, WindowsScreenCaptureService>();
            }
            else
            {
                // Stub or throw
                throw new PlatformNotSupportedException("This OS is not supported yet.");
            }

            // Other Services
            services.AddSingleton<IOcrService, PaddleOcrService>();
            services.AddSingleton<ITranslationServiceFactory, TranslationServiceFactory>();
            services.AddSingleton<PageNavigationService>();

            // AutoMapper
            services.AddSingleton<AutoMapper.IMapper>(sp =>
            {
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var configExpression = new AutoMapper.MapperConfigurationExpression();
                configExpression.AddMaps(typeof(App).Assembly);
                var mapperConfig = new AutoMapper.MapperConfiguration(configExpression, loggerFactory);
                return mapperConfig.CreateMapper();
            });
            // Pages (Register as Page so IEnumerable<Page> works)
            services.AddTransient<Page, HomeViewModel>();
            services.AddTransient<Page, SettingViewModel>();
            services.AddTransient<Page, ShortcutViewModel>();
            services.AddTransient<Page, AboutViewModel>();
            services.AddTransient<Page, PromptViewModel>();

            // ViewModels
            services.AddSingleton<MainWindowViewModel>();

            // Shortcut Action Handlers
            services.AddSingleton<IShortcutActionHandler, ScreenshotTranslateHandler>();
            services.AddSingleton<IShortcutActionHandler, InputTranslateHandler>();
            services.AddSingleton<IShortcutActionHandler, SwitchEngineHandler>();

            // Global Shortcuts Service
            services.AddSingleton<GlobalShortcutService>();

            // Language Services
            services.AddSingleton<LanguageService>();
            
            // Language Code Providers
            services.AddSingleton<ILanguageCodeProvider, BaiduLanguageCodeProvider>();
            services.AddSingleton<ILanguageCodeProvider, TencentLanguageCodeProvider>();
            services.AddSingleton<ILanguageCodeProvider, GoogleLanguageCodeProvider>();
            services.AddSingleton<ILanguageCodeProvider, DeepLLanguageCodeProvider>();
            services.AddSingleton<ILanguageCodeProvider, AiLanguageCodeProvider>();

            // Build Provider
            var provider = services.BuildServiceProvider();
            Global.Services = provider;

            // Ensure ConfigurationService and GlobalShortcutService are initialized
            provider.GetRequiredService<IConfigurationService>();
            provider.GetRequiredService<GlobalShortcutService>();

            // Startup Main Window
            var mainViewModel = provider.GetRequiredService<MainWindowViewModel>();

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            desktop.Exit += (sender, args) =>
            {
                provider.GetRequiredService<IHotKeyManager>().Dispose();
                Log.CloseAndFlush();
                if (provider is IDisposable disposableProvider) disposableProvider.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}