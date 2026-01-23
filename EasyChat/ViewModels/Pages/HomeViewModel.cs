using EasyChat.Lang;
using EasyChat.Models;
using EasyChat.Models.Configuration;
using EasyChat.Services.Abstractions;
using Material.Icons;
using SukiUI.Dialogs;

namespace EasyChat.ViewModels.Pages;

public class HomeViewModel : Page
{
    private readonly ISukiDialogManager _dialogManager;
    private readonly IConfigurationService _configService;

    public HomeViewModel(ISukiDialogManager dialogManager, IConfigurationService configService) 
        : base(Resources.Home, MaterialIconKind.Home)
    {
        _dialogManager = dialogManager;
        _configService = configService;
    }

    /// <summary>
    /// General configuration for binding in XAML
    /// </summary>
    public General GeneralConfig => _configService.General;
}