using System.Threading.Tasks;
using EasyChat.Lang;
using EasyChat.Models;
using EasyChat.Models.Configuration;
using EasyChat.Services.Abstractions;
using Material.Icons;
using ReactiveUI;

namespace EasyChat.ViewModels.Pages;

public class HomeViewModel : Page
{
    private readonly IConfigurationService _configService;
    private readonly Services.UpdateCheckService _updateCheckService;

    public HomeViewModel(IConfigurationService configService, Services.UpdateCheckService updateCheckService) 
        : base(Resources.Home, MaterialIconKind.Home)
    {
        _configService = configService;
        _updateCheckService = updateCheckService;
        
        // Fire and forget update check
        _ = CheckForUpdate();
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
}