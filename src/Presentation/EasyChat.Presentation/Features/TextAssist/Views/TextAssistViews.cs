using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using EasyChat.Presentation.Features.TextAssist.Controls;
using EasyChat.Presentation.Lang;
using EasyChat.Presentation.Foundation.Platform;
using EasyChat.Presentation.Shared.Feedback;
using EasyChat.Presentation.Features.TextAssist;
using EasyChat.Presentation.Features.TextAssist.Views;
using Microsoft.Extensions.Logging;
using SukiUI.Controls;

namespace EasyChat.Presentation.Features.TextAssist.Views
{
    public partial class TextAssistView : UserControl
    {
        public TextAssistView() => InitializeComponent();
    }

    public partial class TextAssistTranslationView : UserControl
    {
        public TextAssistTranslationView() => InitializeComponent();

        private async void OnCopyClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not TextAssistTranslationViewModel viewModel
                || string.IsNullOrWhiteSpace(viewModel.TranslationResult))
                return;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) return;
            await clipboard.SetTextAsync(viewModel.TranslationResult);
            CopyFeedback.Show(sender as Control, EasyChat.Presentation.Lang.Resources.Copied);
        }
    }

    public partial class TextAssistCorrectionView : UserControl
    {
        public TextAssistCorrectionView() => InitializeComponent();

        private void OnOriginalPointerMoved(object? sender, PointerEventArgs e)
        {
            var annotation = AnnotationLayer ?? this.FindControl<CorrectionAnnotationLayer>("AnnotationLayer");
            var hint = CorrectionHint ?? this.FindControl<Border>("CorrectionHint");
            var hintText = CorrectionHintText ?? this.FindControl<TextBlock>("CorrectionHintText");
            if (annotation is null || hint is null || hintText is null) return;
            var issue = annotation.GetIssueAt(e.GetPosition(annotation));
            if (issue is null)
            {
                hint.IsVisible = false;
                return;
            }
            hintText.Text = $"{issue.Message}\n{issue.Suggestion}";
            PositionHint(hint, e);
        }

        private void OnOriginalPointerExited(object? sender, PointerEventArgs e)
        {
            var hint = CorrectionHint ?? this.FindControl<Border>("CorrectionHint");
            if (hint is not null) hint.IsVisible = false;
        }

        private async void OnCopyCorrectionClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: CorrectionVariant variant }) return;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) return;
            await clipboard.SetTextAsync(variant.Text);
            CopyFeedback.Show(sender as Control, EasyChat.Presentation.Lang.Resources.Copied);
        }

        private void PositionHint(Border hint, PointerEventArgs e)
        {
            var host = hint.Parent as Visual ?? this;
            var pointer = e.GetPosition(host);
            hint.RenderTransform = new TranslateTransform(
                Math.Clamp(pointer.X + 14, 0, Math.Max(0, host.Bounds.Width - hint.Bounds.Width)),
                Math.Clamp(pointer.Y + 14, 0, Math.Max(0, host.Bounds.Height - hint.Bounds.Height)));
            hint.IsVisible = true;
        }
    }
}

namespace EasyChat.Presentation.Features.TextAssist.Views
{
    public partial class TextAssistWindowView : SukiWindow
    {
        private TextAssistViewModel? _viewModel;
        private ContentControl? _editorHost;
        private bool _correction;

        public TextAssistWindowView() => InitializeComponent();

        public TextAssistWindowView(TextAssistViewModel viewModel) : this()
        {
            _viewModel = viewModel;
            DataContext = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Loaded += OnLoaded;
            Closed += (_, _) =>
            {
                viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                viewModel.Cancel();
            };
            KeyDown += (_, args) => { if (args.Key == Key.Escape) Close(); };
        }

        public Task InitializeAsync(string text, bool correction)
        {
            _correction = correction;
            ApplyEditor();
            return _viewModel!.InitializeAsync(text, correction);
        }

        public void PrepareForInputCapture(bool correction)
        {
            _correction = correction;
            _viewModel!.PrepareForInputCapture(correction);
            ApplyEditor();
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            _editorHost ??= this.FindControl<ContentControl>("EditorHost");
            ApplyEditor();
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(TextAssistViewModel.SelectedTabIndex)
                or nameof(TextAssistViewModel.IsCorrectionMode)
                or nameof(TextAssistViewModel.IsTranslationMode))
            {
                _correction = _viewModel?.IsCorrectionMode == true;
                ApplyEditor();
            }
        }

        private void ApplyEditor()
        {
            if (_editorHost is null || _viewModel is null) return;
            var wantCorrection = _viewModel.IsCorrectionMode || _correction;
            // Avoid rebuilding the same editor on every property noise.
            if (_editorHost.Content is TextAssistCorrectionView && wantCorrection)
                return;
            if (_editorHost.Content is TextAssistTranslationView && !wantCorrection)
                return;
            _editorHost.Content = wantCorrection
                ? new TextAssistCorrectionView { DataContext = _viewModel.Correction, Classes = { "Compact" } }
                : new TextAssistTranslationView { DataContext = _viewModel.Translation, Classes = { "Compact" } };
        }

        private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        }

        // Segmented radios live in the drag header; don't start a move drag on them.
        private void OnHeaderChromePointerPressed(object? sender, PointerPressedEventArgs e) =>
            e.Handled = true;

        private void OnResizePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control { Tag: string edgeName }
                && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
                && Enum.TryParse<WindowEdge>(edgeName, out var edge))
                BeginResizeDrag(edge, e);
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
    }

    public partial class TextAssistActionWindowView : SukiWindow
    {
        public TextAssistActionWindowView() => InitializeComponent();

        private async void OnCopyClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not TextAssistResultWindowViewModel viewModel)
                return;
            var text = viewModel.CopyText;
            if (string.IsNullOrWhiteSpace(text))
                return;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
                return;
            await clipboard.SetTextAsync(text);
            CopyFeedback.Show(sender as Control, EasyChat.Presentation.Lang.Resources.Copied);
        }
    }

    public partial class TextAssistResultWindowView : Window
    {
        private TextAssistResultWindowViewModel? _viewModel;
        private IPlatformWindowBehavior? _platformWindowBehavior;
        private ILogger<TextAssistResultWindowView>? _logger;

        public TextAssistResultWindowView()
        {
            InitializeComponent();
            PointerPressed += OnSurfacePointerPressed;
        }

        public TextAssistResultWindowView(
            TextAssistResultWindowViewModel viewModel,
            IPlatformWindowBehavior platformWindowBehavior,
            ILogger<TextAssistResultWindowView> logger)
            : this()
        {
            _viewModel = viewModel;
            _platformWindowBehavior = platformWindowBehavior;
            _logger = logger;
            DataContext = viewModel;
            Opened += OnOpened;
            Closed += OnClosed;
            KeyDown += (_, args) => { if (args.Key == Key.Escape) Close(); };
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        public Task InitializeAsync(string text, EasyChat.Contracts.TextAssist.TextAssistOperation operation) =>
            _viewModel!.InitializeAsync(text, operation);

        private async void OnOpened(object? sender, EventArgs e)
        {
            try
            {
                await _platformWindowBehavior!.ConfigureNoActivateAsync(this);
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "Unable to configure the text assist result window as non-activating.");
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _viewModel?.Cancel();
            if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        private async void OnCopyClick(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_viewModel?.CopyText)) return;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) return;
            await clipboard.SetTextAsync(_viewModel.CopyText);
            CopyFeedback.Show(sender as Control, EasyChat.Presentation.Lang.Resources.Copied);
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

        private void OnSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
                && !IsInteractivePointerSource(e.Source))
                BeginMoveDrag(e);
        }

        private static bool IsInteractivePointerSource(object? source)
        {
            if (source is not Visual visual)
                return false;
            if (visual is InputElement { Focusable: true })
                return true;
            return visual.GetVisualAncestors()
                .TakeWhile(ancestor => ancestor is not Window)
                .OfType<InputElement>()
                .Any(element => element.Focusable);
        }

        private void OnResizePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control { Tag: string edgeName }
                && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
                && Enum.TryParse<WindowEdge>(edgeName, out var edge))
            {
                BeginResizeDrag(edge, e);
                e.Handled = true;
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TextAssistResultWindowViewModel.IsCorrectionCorrect))
                Height = _viewModel!.IsCorrectionCorrect ? 220 : 420;
        }

        private void OnOriginalPointerMoved(object? sender, PointerEventArgs e)
        {
            var annotation = this.FindControl<CorrectionAnnotationLayer>("AnnotationLayer");
            var hint = this.FindControl<Border>("CorrectionHint");
            var hintText = this.FindControl<TextBlock>("CorrectionHintText");
            if (annotation is null || hint is null || hintText is null) return;
            var issue = annotation.GetIssueAt(e.GetPosition(annotation));
            if (issue is null)
            {
                hint.IsVisible = false;
                return;
            }
            hintText.Text = $"{issue.Message}\n{issue.Suggestion}";
            var host = hint.Parent as Visual ?? this;
            var pointer = e.GetPosition(host);
            hint.RenderTransform = new TranslateTransform(
                Math.Clamp(pointer.X + 12, 0, Math.Max(0, host.Bounds.Width - hint.Bounds.Width)),
                Math.Clamp(pointer.Y + 12, 0, Math.Max(0, host.Bounds.Height - hint.Bounds.Height)));
            hint.IsVisible = true;
        }

        private void OnOriginalPointerExited(object? sender, PointerEventArgs e)
        {
            var hint = this.FindControl<Border>("CorrectionHint");
            if (hint is not null) hint.IsVisible = false;
        }
    }
}
