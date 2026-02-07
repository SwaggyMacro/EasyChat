using System.Threading.Tasks;
using System.Collections.Generic;
using EasyChat.Lang;
using EasyChat.Models;
using EasyChat.Models.Configuration;
using EasyChat.Services.Abstractions;
using EasyChat.Constants;
using EasyChat.ViewModels.AiModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive;
using Material.Icons;
using ReactiveUI;

namespace EasyChat.ViewModels.Pages;

public class HomeViewModel : Page
{
    private readonly IConfigurationService _configService;
    private readonly Services.UpdateCheckService _updateCheckService;
    
    // Properties for binding
    private ObservableCollection<CustomAiModel> _configuredModels = new();
    public ObservableCollection<CustomAiModel> ConfiguredModels
    {
        get => _configuredModels;
        set => this.RaiseAndSetIfChanged(ref _configuredModels, value);
    }

    private List<string> _machineTransProviders = [Constant.MachineTranslationProviders.Baidu, Constant.MachineTranslationProviders.Tencent, Constant.MachineTranslationProviders.Google, Constant.MachineTranslationProviders.DeepL];
    public List<string> MachineTransProviders
    {
        get => _machineTransProviders;
        set => this.RaiseAndSetIfChanged(ref _machineTransProviders, value);
    }


    public HomeViewModel(IConfigurationService configService, Services.UpdateCheckService updateCheckService) 
        : base(Resources.Home, MaterialIconKind.Home)
    {
        _configService = configService;
        _updateCheckService = updateCheckService;
        
        // Fire and forget update check
        _ = CheckForUpdate();

        RefreshConfiguredModels();

         // Watch for collection changes
        if (AiModelConf != null)
        {
            Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                    h => AiModelConf.ConfiguredModels.CollectionChanged += h,
                    h => AiModelConf.ConfiguredModels.CollectionChanged -= h)
                .Select(_ => Unit.Default)
                .Subscribe(Observer.Create<Unit>(_ => RefreshConfiguredModels()));
        }
    }

    public AiModel? AiModelConf => _configService.AiModel;

    private void RefreshConfiguredModels()
    {
        if (AiModelConf == null) return;
        ConfiguredModels = new ObservableCollection<CustomAiModel>(AiModelConf.ConfiguredModels);
    }

    private async Task CheckForUpdate()
    {
        try
        {
            var updateInfo = await _updateCheckService.CheckForUpdateAsync();
            if (updateInfo != null)
            {
                LatestVersion = updateInfo.TargetFullRelease.Version.ToString();
            }
            else
            {
                LatestVersion = CurrentVersion;
            }
        }
        catch
        {
            LatestVersion = "Error";
        }
    }

    /// <summary>
    /// General configuration for binding in XAML
    /// </summary>
    public General? GeneralConfig => _configService.General;

    /// <summary>
    /// Application version
    /// </summary>
    public string CurrentVersion
    {
        get
        {
            var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "Unknown";
        }
    }

    /// <summary>
    /// Latest version from GitHub
    /// </summary>
    public string LatestVersion
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "-";

    /// <summary>
    /// Available languages for selection
    /// </summary>
    public IEnumerable<Services.Languages.LanguageDefinition> AvailableLanguages => Services.Languages.LanguageService.GetAllLanguages();
}