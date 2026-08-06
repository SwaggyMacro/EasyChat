using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using EasyChat.Contracts.Platform;
using EasyChat.Contracts.Settings;
using EasyChat.Presentation.Foundation.Platform;
using EasyChat.Presentation.Features.Speech;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace EasyChat.Presentation.Features.Speech.Views
{
    public partial class SpeechRecognitionView : UserControl
    {
        private bool _isLoaded;

        public SpeechRecognitionView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object? sender, RoutedEventArgs eventArgs)
        {
            if (_isLoaded)
                return;
            _isLoaded = true;
            if (DataContext is SpeechRecognitionViewModel viewModel)
                await viewModel.InitializeAsync();
            await Task.Delay(200);
            MainContent.IsVisible = true;
            LoadingOverlay.IsVisible = false;
        }
    }
}

namespace EasyChat.Presentation.Features.Speech.Views
{
    public partial class SubtitleOverlayWindowView : Window
    {
        private readonly List<IDisposable> _subscriptions = [];
        private IPlatformWindowBehavior? _platformWindowBehavior;
        private IPointerPosition? _pointer;
        private ILogger<SubtitleOverlayWindowView>? _logger;
        private DispatcherTimer? _hitTestTimer;
        private SpeechRecognitionViewModel? _viewModel;
        private bool _isClickThrough;

        public SubtitleOverlayWindowView()
        {
            InitializeComponent();
            PointerPressed += OnPointerPressed;
        }

        public SubtitleOverlayWindowView(
            SpeechRecognitionViewModel viewModel,
            IPlatformWindowBehavior platformWindowBehavior,
            IPointerPosition pointer,
            ILogger<SubtitleOverlayWindowView> logger)
            : this()
        {
            _platformWindowBehavior = platformWindowBehavior;
            _pointer = pointer;
            _logger = logger;
            DataContext = viewModel;
        }

        protected override void OnOpened(EventArgs eventArgs)
        {
            base.OnOpened(eventArgs);
            if (_viewModel is not null)
                ApplyLockState(_viewModel.IsFloatingWindowLocked);
        }

        protected override void OnClosed(EventArgs eventArgs)
        {
            StopHitTestTimer();
            DetachViewModel();
            base.OnClosed(eventArgs);
        }

        protected override void OnDataContextChanged(EventArgs eventArgs)
        {
            base.OnDataContextChanged(eventArgs);
            DetachViewModel();
            if (DataContext is not SpeechRecognitionViewModel viewModel)
                return;
            _viewModel = viewModel;
            _subscriptions.Add(viewModel.WhenAnyValue(model => model.IsFloatingWindowLocked)
                .Subscribe(ApplyLockState));
            _subscriptions.Add(viewModel.WhenAnyValue(model => model.FloatingWindowOrientation)
                .Subscribe(UpdateOrientation));
            _subscriptions.Add(viewModel.WhenAnyValue(model => model.FloatingDisplayMode)
                .Subscribe(mode =>
                {
                    if (ShouldFollowLatest(
                            mode,
                            viewModel.FloatingSubtitles.Count,
                            viewModel.MaxFloatingHistory))
                        TriggerAutoScroll();
                }));
            _subscriptions.Add(viewModel.WhenAnyValue(model => model.MaxFloatingHistory)
                .Subscribe(limit =>
                {
                    if (ShouldFollowLatest(
                            viewModel.FloatingDisplayMode,
                            viewModel.FloatingSubtitles.Count,
                            limit))
                        TriggerAutoScroll();
                }));
            foreach (var item in viewModel.FloatingSubtitles)
                item.PropertyChanged += OnSubtitlePropertyChanged;
            viewModel.FloatingSubtitles.CollectionChanged += OnFloatingSubtitlesChanged;
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
        {
            // Locked: body is click-through; only LockedDragHandle starts a move.
            if (_viewModel?.IsFloatingWindowLocked == true)
                return;
            if (eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(eventArgs);
        }

        private void OnLockedDragPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
        {
            if (_viewModel?.IsFloatingWindowLocked != true)
                return;
            if (!eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;
            // Ensure we own the pointer before starting a system move drag.
            eventArgs.Handled = true;
            _ = EnsureInteractiveThenMoveAsync(eventArgs);
        }

        private async Task EnsureInteractiveThenMoveAsync(PointerPressedEventArgs eventArgs)
        {
            await SetClickThroughAsync(false);
            // Yield so the platform applies WS_EX_TRANSPARENT clear before drag.
            await Task.Yield();
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    BeginMoveDrag(eventArgs);
                }
                catch (Exception exception)
                {
                    _logger?.LogDebug(exception, "Locked subtitle drag failed.");
                }
            }, DispatcherPriority.Input);
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs eventArgs) => Close();

        private void OnResizePointerPressed(object? sender, PointerPressedEventArgs eventArgs)
        {
            if (_viewModel?.IsFloatingWindowLocked != true
                && eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                SizeToContent = SizeToContent.Manual;
                BeginResizeDrag(WindowEdge.SouthEast, eventArgs);
            }
        }

        private void ApplyLockState(bool isLocked)
        {
            Background = isLocked ? null : Brushes.Transparent;
            RootGrid.Background = null;
            if (LockedChrome is not null)
                LockedChrome.Opacity = isLocked ? 0 : 1;
            if (isLocked)
            {
                _ = SetClickThroughAsync(true);
                StartHitTestTimer();
            }
            else
            {
                StopHitTestTimer();
                _ = SetClickThroughAsync(false);
                if (LockedChrome is not null)
                    LockedChrome.Opacity = 0;
            }
        }

        private void UpdateOrientation(string orientation)
        {
            if (orientation == "Vertical")
            {
                MinWidth = 60;
                MaxWidth = 150;
                if (Width > 150 || double.IsNaN(Width))
                    Width = 100;
                if (SizeToContent != SizeToContent.Manual)
                    SizeToContent = SizeToContent.Height;
                return;
            }

            MinWidth = 200;
            MaxWidth = double.PositiveInfinity;
            if (Width < 200 || double.IsNaN(Width))
                Width = 800;
            SizeToContent = SizeToContent.Manual;
        }

        private void OnFloatingSubtitlesChanged(
            object? sender,
            NotifyCollectionChangedEventArgs eventArgs)
        {
            if (eventArgs.NewItems is not null)
            {
                foreach (SpeechSubtitleItemViewModel item in eventArgs.NewItems)
                    item.PropertyChanged += OnSubtitlePropertyChanged;
            }
            if (eventArgs.OldItems is not null)
            {
                foreach (SpeechSubtitleItemViewModel item in eventArgs.OldItems)
                    item.PropertyChanged -= OnSubtitlePropertyChanged;
            }
            if (_viewModel is not null
                && ShouldFollowLatest(
                    _viewModel.FloatingDisplayMode,
                    _viewModel.FloatingSubtitles.Count,
                    _viewModel.MaxFloatingHistory))
                TriggerAutoScroll();
        }

        private void OnSubtitlePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
        {
            if (_viewModel is not null
                && ShouldFollowLatest(
                    _viewModel.FloatingDisplayMode,
                    _viewModel.FloatingSubtitles.Count,
                    _viewModel.MaxFloatingHistory)
                && eventArgs.PropertyName is nameof(SpeechSubtitleItemViewModel.OriginalText)
                    or nameof(SpeechSubtitleItemViewModel.DisplayTranslatedText)
                    or nameof(SpeechSubtitleItemViewModel.TranslatedText))
            {
                TriggerAutoScroll();
            }
        }

        private async void TriggerAutoScroll()
        {
            await Task.Delay(50);
            Dispatcher.UIThread.Post(
                () => SubtitlesScrollViewer.ScrollToEnd(),
                DispatcherPriority.Background);
        }

        internal static bool UsesAutoScroll(FloatingDisplayMode mode) =>
            mode == FloatingDisplayMode.AutoScroll;

        internal static bool ShouldFollowLatest(
            FloatingDisplayMode mode,
            int visibleCount,
            int completedHistoryLimit) =>
            UsesAutoScroll(mode) || visibleCount > Math.Max(1, completedHistoryLimit);

        private void StartHitTestTimer()
        {
            _hitTestTimer ??= new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _hitTestTimer.Tick -= OnHitTestTick;
            _hitTestTimer.Tick += OnHitTestTick;
            _hitTestTimer.Start();
        }

        private void StopHitTestTimer() => _hitTestTimer?.Stop();

        private void OnHitTestTick(object? sender, EventArgs eventArgs)
        {
            if (_pointer is null || LockedChrome is null || !LockedChrome.IsVisible)
                return;

            var position = _pointer.GetCurrent();
            var point = new PixelPoint(position.X, position.Y);
            var windowTopLeft = Position;
            var windowRect = new PixelRect(
                windowTopLeft,
                PixelSize.FromSize(Bounds.Size, RenderScaling));

            // Grow hit target slightly so the chrome is easy to catch while locked.
            var chromeTopLeft = LockedChrome.PointToScreen(new Point(-6, -6));
            var chromeSize = PixelSize.FromSize(
                new Size(LockedChrome.Bounds.Width + 12, LockedChrome.Bounds.Height + 12),
                RenderScaling);
            var chromeRect = new PixelRect(chromeTopLeft, chromeSize);

            var nearWindow = windowRect.Contains(point);
            LockedChrome.Opacity = nearWindow || chromeRect.Contains(point) ? 1 : 0;

            // Only the locked chrome (drag + unlock) captures input; rest stays click-through.
            if (chromeRect.Contains(point) && _isClickThrough)
                _ = SetClickThroughAsync(false);
            else if (!chromeRect.Contains(point) && !_isClickThrough)
                _ = SetClickThroughAsync(true);
        }

        private async Task SetClickThroughAsync(bool enabled)
        {
            if (_platformWindowBehavior is null || _isClickThrough == enabled)
                return;
            try
            {
                await _platformWindowBehavior.SetClickThroughAsync(this, enabled);
                _isClickThrough = enabled;
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "Unable to update subtitle click-through state.");
            }
        }

        private void DetachViewModel()
        {
            foreach (var subscription in _subscriptions)
                subscription.Dispose();
            _subscriptions.Clear();
            if (_viewModel is not null)
            {
                _viewModel.FloatingSubtitles.CollectionChanged -= OnFloatingSubtitlesChanged;
                foreach (var item in _viewModel.FloatingSubtitles)
                    item.PropertyChanged -= OnSubtitlePropertyChanged;
            }
            _viewModel = null;
        }
    }
}

namespace EasyChat.Presentation.Features.Speech.Views
{
    public partial class TtsEditVoiceDialogView : UserControl
    {
        public TtsEditVoiceDialogView() => InitializeComponent();
    }

    public partial class TtsPreviewInputDialogView : UserControl
    {
        public TtsPreviewInputDialogView() => InitializeComponent();
    }

    public partial class TtsVoiceSettingsDialogView : UserControl
    {
        public TtsVoiceSettingsDialogView() => InitializeComponent();
    }
}
