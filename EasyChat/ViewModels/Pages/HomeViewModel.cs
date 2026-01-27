using EasyChat.Lang;
using EasyChat.Models;
using EasyChat.Models.Configuration;
using EasyChat.Services.Abstractions;
using Material.Icons;

namespace EasyChat.ViewModels.Pages;

public class HomeViewModel : Page
{
    private readonly IConfigurationService _configService;

    public HomeViewModel(IConfigurationService configService) 
        : base(Resources.Home, MaterialIconKind.Home)
    {
        _configService = configService;
    }

    /// <summary>
    /// General configuration for binding in XAML
    /// </summary>
    public General? GeneralConfig => _configService.General;
}