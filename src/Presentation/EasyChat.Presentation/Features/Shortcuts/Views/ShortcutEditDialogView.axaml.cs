using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace EasyChat.Presentation.Features.Shortcuts.Views;

/// <summary>
/// Hotkey capture for the edit dialog.
/// Uses three paths because Suki dialog chrome + ScrollViewer often starve a single path:
/// 1) dedicated focusable capture pad KeyDown
/// 2) TopLevel tunnel+bubble with handledEventsToo
/// 3) Window.KeyDown CLR event
/// </summary>
public partial class ShortcutEditDialogView : UserControl
{
    private TopLevel? _keyHost;
    private Window? _window;
    private ShortcutEditDialogViewModel? _subscribedVm;
    private bool _handlersAttached;

    public ShortcutEditDialogView()
    {
        InitializeComponent();
        Focusable = true;
        IsTabStop = true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_subscribedVm is not null)
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;

        _subscribedVm = DataContext as ShortcutEditDialogViewModel;
        if (_subscribedVm is not null)
            _subscribedVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachKeyHandlers(TopLevel.GetTopLevel(this));

        // Start recording only after the view can take focus (not in VM ctor).
        Dispatcher.UIThread.Post(() =>
        {
            if (_subscribedVm is { IsRecording: false, IsRecordingBeforeInputKey: false, IsRecordingAfterInputKey: false }
                && string.IsNullOrWhiteSpace(_subscribedVm.KeyCombination))
            {
                _subscribedVm.BeginPrimaryRecording();
            }

            FocusCapturePad();
        }, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachKeyHandlers();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ShortcutEditDialogViewModel.IsRecording)
            or nameof(ShortcutEditDialogViewModel.IsRecordingBeforeInputKey)
            or nameof(ShortcutEditDialogViewModel.IsRecordingAfterInputKey))
        {
            Dispatcher.UIThread.Post(FocusCapturePad, DispatcherPriority.Input);
        }
    }

    private void AttachKeyHandlers(TopLevel? topLevel)
    {
        DetachKeyHandlers();
        _keyHost = topLevel;
        if (_keyHost is null)
            return;

        // handledEventsToo: TextBox/Button may mark keys handled before we see them otherwise.
        _keyHost.AddHandler(
            InputElement.KeyDownEvent,
            OnRoutedKeyDown,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        _keyHost.AddHandler(
            InputElement.KeyUpEvent,
            OnRoutedKeyUp,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);

        _window = _keyHost as Window ?? this.FindAncestorOfType<Window>();
        if (_window is not null)
        {
            _window.KeyDown += OnWindowKeyDown;
            _window.KeyUp += OnWindowKeyUp;
        }

        _handlersAttached = true;
    }

    private void DetachKeyHandlers()
    {
        if (!_handlersAttached)
            return;

        if (_keyHost is not null)
        {
            _keyHost.RemoveHandler(InputElement.KeyDownEvent, OnRoutedKeyDown);
            _keyHost.RemoveHandler(InputElement.KeyUpEvent, OnRoutedKeyUp);
        }

        if (_window is not null)
        {
            _window.KeyDown -= OnWindowKeyDown;
            _window.KeyUp -= OnWindowKeyUp;
        }

        _keyHost = null;
        _window = null;
        _handlersAttached = false;
    }

    private void FocusCapturePad()
    {
        if (this.FindControl<Border>("HotkeyCapturePad") is { } pad)
        {
            pad.Focus(NavigationMethod.Pointer);
            return;
        }

        Focus(NavigationMethod.Pointer);
    }

    private void OnCapturePadPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_subscribedVm is null)
            return;

        // Click the pad to (re)arm recording — same as the old toggle button.
        if (_subscribedVm.IsRecording)
            _subscribedVm.StopRecording();
        else
            _subscribedVm.BeginPrimaryRecording();

        FocusCapturePad();
        e.Handled = true;
    }

    private void OnCapturePadKeyDown(object? sender, KeyEventArgs e) => HandleKeyDown(e);

    private void OnRoutedKeyDown(object? sender, KeyEventArgs e) => HandleKeyDown(e);

    private void OnWindowKeyDown(object? sender, KeyEventArgs e) => HandleKeyDown(e);

    private void OnRoutedKeyUp(object? sender, KeyEventArgs e)
    {
        if (DataContext is ShortcutEditDialogViewModel viewModel && IsRecording(viewModel))
            e.Handled = true;
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (DataContext is ShortcutEditDialogViewModel viewModel && IsRecording(viewModel))
            e.Handled = true;
    }

    private void OnSelectionToolbarInfoPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control control)
            control.SetValue(ToolTip.IsOpenProperty, true);
    }

    private void OnSelectionToolbarInfoPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control control)
            control.SetValue(ToolTip.IsOpenProperty, false);
    }

    private void HandleKeyDown(KeyEventArgs e)
    {
        if (DataContext is not ShortcutEditDialogViewModel viewModel || !IsRecording(viewModel))
            return;

        if (e.Key == Key.Escape)
        {
            viewModel.StopRecording();
            e.Handled = true;
            return;
        }

        // Keep Tab for moving focus only when not actively recording a chord finish.
        if (e.Key == Key.Tab)
        {
            viewModel.StopRecording();
            return;
        }

        var keyName = ResolveKeyName(e);
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                      || e.Key is Key.LeftCtrl or Key.RightCtrl;
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt)
                  || e.Key is Key.LeftAlt or Key.RightAlt;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                    || e.Key is Key.LeftShift or Key.RightShift;
        var meta = e.KeyModifiers.HasFlag(KeyModifiers.Meta)
                   || e.Key is Key.LWin or Key.RWin;

        var isModifier = e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None
            || string.Equals(keyName, "None", StringComparison.OrdinalIgnoreCase);

        var combination = new StringBuilder();
        if (control) combination.Append("Ctrl + ");
        if (alt) combination.Append("Alt + ");
        if (shift) combination.Append("Shift + ");
        if (meta) combination.Append("Win + ");
        if (!isModifier && !string.IsNullOrWhiteSpace(keyName))
            combination.Append(keyName);

        var currentCombination = combination.ToString();
        if (string.IsNullOrWhiteSpace(currentCombination))
            return;

        viewModel.PreviewRecordedKeyCombination(currentCombination);
        if (!isModifier)
            viewModel.SetRecordedKeyCombination(currentCombination);

        e.Handled = true;
    }

    private static string ResolveKeyName(KeyEventArgs e)
    {
        if (e.Key is not Key.None)
            return e.Key.ToString();

        // Avalonia 11+/12 sometimes reports Key.None for OEM keys; fall back.
        if (e.KeySymbol is { Length: > 0 } symbol && !char.IsControl(symbol[0]))
            return symbol.ToUpperInvariant();

        var physical = e.PhysicalKey.ToString();
        return string.IsNullOrWhiteSpace(physical) || physical is "None" or "Unidentified"
            ? string.Empty
            : physical;
    }

    private static bool IsRecording(ShortcutEditDialogViewModel viewModel) =>
        viewModel.IsRecording || viewModel.IsRecordingBeforeInputKey || viewModel.IsRecordingAfterInputKey;
}
