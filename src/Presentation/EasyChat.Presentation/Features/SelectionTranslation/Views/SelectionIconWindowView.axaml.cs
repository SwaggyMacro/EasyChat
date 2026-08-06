using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EasyChat.Contracts.Selection;
using EasyChat.Presentation.Foundation.Platform;
using Material.Icons.Avalonia;
using Microsoft.Extensions.Logging;
using SukiUI.Controls;

namespace EasyChat.Presentation.Features.SelectionTranslation.Views;

public partial class SelectionIconWindowView : Window
{
    private const double ShellPadding = 14;
    private const double ButtonGap = 3;
    private const double CompactButton = 30;
    private const double NormalButton = 34;
    private const double PrimaryExtraWidth = 52;

    private IPlatformWindowBehavior? _platformWindowBehavior;
    private ILogger<SelectionIconWindowView>? _logger;
    private readonly Loading? _loadingSpinner;
    private readonly Control? _toolbar;
    private readonly Button? _translateButton;
    private readonly Button? _correctionButton;
    private readonly Button? _polishButton;
    private readonly Button? _summaryButton;
    private readonly Button? _explanationButton;
    private bool _isLoading;

    public SelectionIconWindowView()
    {
        InitializeComponent();
        _loadingSpinner = this.FindControl<Loading>("LoadingSpinner");
        _toolbar = this.FindControl<Control>("Toolbar");
        _translateButton = this.FindControl<Button>("TranslateButton");
        _correctionButton = this.FindControl<Button>("CorrectionButton");
        _polishButton = this.FindControl<Button>("PolishButton");
        _summaryButton = this.FindControl<Button>("SummaryButton");
        _explanationButton = this.FindControl<Button>("ExplanationButton");
    }

    public SelectionIconWindowView(
        IPlatformWindowBehavior platformWindowBehavior,
        ILogger<SelectionIconWindowView> logger)
        : this()
    {
        _platformWindowBehavior = platformWindowBehavior;
        _logger = logger;
        Opened += OnOpened;
    }

    public event EventHandler? TranslateClicked;
    public event EventHandler? CorrectionClicked;
    public event EventHandler? PolishClicked;
    public event EventHandler? SummaryClicked;
    public event EventHandler? ExplanationClicked;

    public bool IsLoading => _isLoading;

    public void Configure(SelectionToolbarOptions options)
    {
        if (_translateButton is not null) _translateButton.IsVisible = options.Translation;
        if (_correctionButton is not null) _correctionButton.IsVisible = options.Correction;
        if (_polishButton is not null) _polishButton.IsVisible = options.Polish;
        if (_summaryButton is not null) _summaryButton.IsVisible = options.Summary;
        if (_explanationButton is not null) _explanationButton.IsVisible = options.Explanation;

        var secondaryCount = (options.Correction ? 1 : 0)
                             + (options.Polish ? 1 : 0)
                             + (options.Summary ? 1 : 0);
        var hasPrimary = options.Translation;
        var totalCount = (hasPrimary ? 1 : 0) + secondaryCount;
        var compact = totalCount == 1;
        var secondarySize = compact ? CompactButton : NormalButton;
        var iconSize = compact ? 15.0 : 17.0;

        if (_translateButton is not null)
        {
            // Primary stays pill-shaped with optional label when not alone.
            _translateButton.MinWidth = secondarySize;
            _translateButton.Height = secondarySize;
            _translateButton.Padding = compact ? new Thickness(8, 4) : new Thickness(10, 6);
            if (this.FindControl<TextBlock>("TranslateLabel") is { } label)
                label.IsVisible = !compact || !hasPrimary || secondaryCount > 0;
        }

        foreach (var button in new[] { _correctionButton, _polishButton, _summaryButton })
        {
            if (button is null)
                continue;
            button.Width = secondarySize;
            button.Height = secondarySize;
            button.Padding = compact ? new Thickness(4) : new Thickness(6);
            if (button.Content is MaterialIcon icon)
            {
                icon.Width = iconSize;
                icon.Height = iconSize;
            }
        }

        var primaryWidth = hasPrimary
            ? (compact && secondaryCount == 0 ? secondarySize + 8 : secondarySize + PrimaryExtraWidth)
            : 0;
        var secondaryWidth = secondaryCount * secondarySize;
        var gaps = Math.Max(0, totalCount - 1) * ButtonGap;
        Width = Math.Max(44, ShellPadding + primaryWidth + secondaryWidth + gaps);
        Height = compact ? 38 : 44;
    }

    public void ShowLoading()
    {
        _isLoading = true;
        if (_toolbar is not null) _toolbar.IsVisible = false;
        if (_loadingSpinner is not null) _loadingSpinner.IsVisible = true;
    }

    public void HideLoading()
    {
        _isLoading = false;
        if (_toolbar is not null) _toolbar.IsVisible = true;
        if (_loadingSpinner is not null) _loadingSpinner.IsVisible = false;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            await _platformWindowBehavior!.ConfigureNoActivateAsync(this);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "Unable to configure the selection toolbar as non-activating.");
        }
    }

    private bool CanInvoke() => !_isLoading;
    private void OnTranslateClick(object? sender, RoutedEventArgs e) { if (CanInvoke()) TranslateClicked?.Invoke(this, EventArgs.Empty); }
    private void OnCorrectionClick(object? sender, RoutedEventArgs e) { if (CanInvoke()) CorrectionClicked?.Invoke(this, EventArgs.Empty); }
    private void OnPolishClick(object? sender, RoutedEventArgs e) { if (CanInvoke()) PolishClicked?.Invoke(this, EventArgs.Empty); }
    private void OnSummaryClick(object? sender, RoutedEventArgs e) { if (CanInvoke()) SummaryClicked?.Invoke(this, EventArgs.Empty); }
    private void OnExplanationClick(object? sender, RoutedEventArgs e) { if (CanInvoke()) ExplanationClicked?.Invoke(this, EventArgs.Empty); }
}
