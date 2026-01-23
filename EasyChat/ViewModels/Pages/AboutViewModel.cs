using System.Diagnostics.CodeAnalysis;
using EasyChat.Lang;
using EasyChat.Models;
using Material.Icons;
using SukiUI.Dialogs;

// Added

namespace EasyChat.ViewModels.Pages;

[SuppressMessage("ReSharper", "NotAccessedField.Local")]
public class AboutViewModel : Page
{
    private readonly ISukiDialogManager _dialogManager;

    public AboutViewModel(ISukiDialogManager dialogManager) : base(Resources.About, MaterialIconKind.About, 10)
    {
        _dialogManager = dialogManager;
    }
}