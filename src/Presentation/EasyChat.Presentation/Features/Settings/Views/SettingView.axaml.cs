using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace EasyChat.Presentation.Features.Settings.Views;

public partial class SettingView : UserControl
{
    private bool _isLoaded;
    private SettingViewModel? _subscribedVm;

    public SettingView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_isLoaded)
            return;

        _isLoaded = true;
        await Task.Delay(200);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            SettingsContent.IsVisible = true;
            LoadingOverlay.IsVisible = false;
        });
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm is not null)
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;

        _subscribedVm = DataContext as SettingViewModel;
        if (_subscribedVm is not null)
            _subscribedVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingViewModel.IsSearchOpen)
            && _subscribedVm is { IsSearchOpen: true })
        {
            Dispatcher.UIThread.Post(FocusSearchBox, DispatcherPriority.Input);
        }
    }

    private void OnOpenSearchClick(object? sender, RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(FocusSearchBox, DispatcherPriority.Input);

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Escape || _subscribedVm is null)
            return;

        _subscribedVm.CollapseSearch();
        e.Handled = true;
    }

    private void FocusSearchBox()
    {
        if (this.FindControl<TextBox>("SettingsSearchBox") is not { } box)
            return;

        box.Focus();
        // Place caret at end so re-open after partial type feels natural.
        box.CaretIndex = box.Text?.Length ?? 0;
    }
}
