using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using Avalonia.Collections;
using Avalonia.Styling;
using EasyChat.Common;
using EasyChat.Controls.CustomTheme;
using EasyChat.Lang;
using EasyChat.Models;
using EasyChat.Services;
using ReactiveUI;
using SukiUI;
using SukiUI.Dialogs;
using SukiUI.Enums;
using SukiUI.Models;
using SukiUI.Toasts;

namespace EasyChat.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly SukiTheme _theme;

    private Page? _activePage;
    private IAvaloniaReadOnlyList<Page> _pages;


    public MainWindowViewModel(
        IEnumerable<Page> pages,
        PageNavigationService pageNavigationService,
        ISukiToastManager toastManager,
        ISukiDialogManager dialogManager)
    {
        // Sort and assign pages
        var sortedPages = pages.OrderBy(x => x.Index).ThenBy(x => x.DisplayName).ToList();
        _pages = new AvaloniaList<Page>(sortedPages);

        // Use the first page as default active page if available
        if (sortedPages.Any()) _activePage = sortedPages.First();

        ToastManager = toastManager;
        DialogManager = dialogManager;

        // Global.ToastManager = toastManager; // Removed assignment as it is read-only

        _theme = SukiTheme.GetInstance();
        Themes = _theme.ColorThemes;
        // BackgroundStyles = _theme.BackgroundStyles; // Removed as it might not exist in this version
        BaseTheme = _theme.ActiveBaseTheme;

        // Commands
        ToggleBaseThemeCommand = ReactiveCommand.Create(ToggleBaseTheme);
        CreateCustomThemeCommand = ReactiveCommand.Create(CreateCustomTheme);
        ToggleTitleBarCommand = ReactiveCommand.Create(ToggleTitleBar);
        OpenUrlCommand = ReactiveCommand.Create<string>(OpenUrl);

        // Navigation
        pageNavigationService.NavigationRequested += pageType =>
        {
            var page = Pages.FirstOrDefault(x => x.GetType() == pageType);
            if (page is null || ActivePage?.GetType() == pageType) return;
            ActivePage = page;
        };

        // Theme Events
        _theme.OnBaseThemeChanged += variant =>
        {
            BaseTheme = variant;
            ToastManager.CreateSimpleInfoToast()
                .WithTitle(Resources.ThemeChangedTitle)
                .WithContent($"{Resources.ThemeChangedContent} {variant}.")
                .Queue();
        };

        _theme.OnColorThemeChanged += theme =>
        {
            ToastManager.CreateSimpleInfoToast()
                .WithTitle(Resources.ColorChangedTitle)
                .WithContent($"{Resources.ColorChangedContent} {theme.DisplayName}.")
                .Queue();
        };
    }

    public IAvaloniaReadOnlyList<Page> Pages
    {
        get => _pages;
        set => this.RaiseAndSetIfChanged(ref _pages, value);
    }

    public IAvaloniaReadOnlyList<SukiColorTheme> Themes { get; }
    public ISukiDialogManager DialogManager { get; }

    public ISukiToastManager
        ToastManager
    {
        get;
    } // Read-only property in VM interface usually, but if I need to set it... well Global has it. 

    public bool TitleBarVisible
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    public Page? ActivePage
    {
        get => _activePage;
        set => this.RaiseAndSetIfChanged(ref _activePage, value);
    }

    public ThemeVariant BaseTheme
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public SukiBackgroundStyle BackgroundStyle
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = SukiBackgroundStyle.Gradient;

    public bool AnimationsEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string? CustomShaderFile
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool TransitionsEnabled
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public double TransitionTime
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<Unit, Unit> ToggleBaseThemeCommand { get; }
    public ReactiveCommand<Unit, Unit> CreateCustomThemeCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleTitleBarCommand { get; }
    public ReactiveCommand<string, Unit> OpenUrlCommand { get; }



    private void ToggleBaseTheme()
    {
        _theme.SwitchBaseTheme();
    }

    public void ChangeTheme(SukiColorTheme theme)
    {
        _theme.ChangeColorTheme(theme);
    }

    private void CreateCustomTheme()
    {
        DialogManager.CreateDialog()
            .WithViewModel(dialog => new CustomThemeDialogViewModel(_theme, dialog))
            .TryShow();
    }

    private void ToggleTitleBar()
    {
        TitleBarVisible = !TitleBarVisible;
        ToastManager.CreateSimpleInfoToast()
            .WithTitle($"{Resources.TitleBarTitle} {(TitleBarVisible ? "Visible" : "Hidden")}")
            .WithContent($"{Resources.TitleBarContent} {(TitleBarVisible ? "shown" : "hidden")}.")
            .Queue();
    }

    private static void OpenUrl(string url)
    {
        UrlUtilities.OpenUrl(url);
    }
}