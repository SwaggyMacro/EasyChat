using System.Reactive;
using Avalonia.Media;
using EasyChat.Presentation.Features.Settings.State;
using EasyChat.Presentation.Foundation.Navigation;
using EasyChat.Presentation.Foundation.UiHost;
using ReactiveUI;
using SukiUI;
using SukiUI.Models;

namespace EasyChat.Presentation.Features.Settings.Theme;

public sealed class CustomThemeDialogViewModel : ConventionViewModelBase
{
    private readonly SukiTheme _theme;
    private readonly IUiDialogSession _dialog;
    private readonly LiveGeneralSettings _settings;
    private string _displayName = "Pink";
    private Color _primaryColor = Colors.DeepPink;
    private Color _accentColor = Colors.Pink;

    public CustomThemeDialogViewModel(SukiTheme theme, IUiDialogSession dialog, LiveGeneralSettings settings)
    {
        _theme = theme;
        _dialog = dialog;
        _settings = settings;
        TryCreateThemeCommand = ReactiveCommand.Create(CreateTheme);
        CancelCommand = ReactiveCommand.Create(dialog.Dismiss);
    }

    public string DisplayName { get => _displayName; set => this.RaiseAndSetIfChanged(ref _displayName, value); }
    public Color PrimaryColor { get => _primaryColor; set => this.RaiseAndSetIfChanged(ref _primaryColor, value); }
    public Color AccentColor { get => _accentColor; set => this.RaiseAndSetIfChanged(ref _accentColor, value); }
    public ReactiveCommand<Unit, Unit> TryCreateThemeCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    private void CreateTheme()
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
            return;
        var theme = new SukiColorTheme(DisplayName, PrimaryColor, AccentColor);
        _theme.AddColorTheme(theme);
        _theme.ChangeColorTheme(theme);
        _settings.ColorTheme = DisplayName;
        _settings.CustomThemePrimaryColor = PrimaryColor.ToString();
        _settings.CustomThemeAccentColor = AccentColor.ToString();
        _dialog.Dismiss();
    }
}
