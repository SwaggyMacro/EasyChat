using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using EasyChat.Presentation.Lang;
using EasyChat.Presentation.Foundation.Platform;
using EasyChat.Presentation.Shared.Feedback;
using Microsoft.Extensions.Logging;

namespace EasyChat.Presentation.Features.Translation.Views
{
    public partial class TranslationDictionaryWindowView : Window
    {
        private IPlatformWindowBehavior? _platformWindowBehavior;
        private ILogger<TranslationDictionaryWindowView>? _logger;
        private readonly ScrollViewer? _contentScrollViewer;

        public TranslationDictionaryWindowView()
        {
            InitializeComponent();
            _contentScrollViewer = this.FindControl<ScrollViewer>("ContentScrollViewer");
            PointerPressed += OnSurfacePointerPressed;
        }

        public TranslationDictionaryWindowView(
            TranslationDictionaryWindowViewModel viewModel,
            IPlatformWindowBehavior platformWindowBehavior,
            ILogger<TranslationDictionaryWindowView> logger)
            : this()
        {
            _platformWindowBehavior = platformWindowBehavior;
            _logger = logger;
            DataContext = viewModel;
            Opened += OnOpened;
            KeyDown += OnKeyDown;
        }

        private async void OnOpened(object? sender, EventArgs e)
        {
            try
            {
                await _platformWindowBehavior!.ConfigureNoActivateAsync(this);
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "Unable to configure the translation window as non-activating.");
            }
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Close();
        }

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

        private async void OnCopyClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not TranslationDictionaryWindowViewModel viewModel)
                return;

            var text = FormatCopyText(viewModel);
            if (string.IsNullOrWhiteSpace(text))
                return;

            try
            {
                var clipboard = GetTopLevel(this)?.Clipboard;
                if (clipboard is null)
                    return;

                await clipboard.SetTextAsync(text);
                CopyFeedback.Show(sender as Control, EasyChat.Presentation.Lang.Resources.Copied);
                _logger?.LogDebug("Copied translation result to clipboard.");
            }
            catch (Exception exception)
            {
                _logger?.LogError(exception, "Unable to copy the translation result.");
            }
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

        private void OnResizePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            // Height is already Manual; scroll viewer fills the star row — no MaxHeight lock.
            SizeToContent = SizeToContent.Manual;
            BeginResizeDrag(WindowEdge.SouthEast, e);
            e.Handled = true;
        }

        private static string FormatCopyText(TranslationDictionaryWindowViewModel viewModel)
        {
            if (!viewModel.IsWordMode || viewModel.DictionaryResult is null)
                return viewModel.TranslationResult;

            var dictionary = viewModel.DictionaryResult;
            var text = new StringBuilder(dictionary.Word);
            if (!string.IsNullOrWhiteSpace(dictionary.Phonetic))
                text.Append(' ').Append(dictionary.Phonetic);
            text.AppendLine();

            foreach (var part in dictionary.Parts)
                text.AppendLine($"{part.PartOfSpeech} {string.Join("; ", part.Definitions)}");

            if (dictionary.Examples.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Examples:");
                foreach (var example in dictionary.Examples)
                    text.AppendLine($"{example.Origin} -> {example.Translation}");
            }

            return text.ToString().Trim();
        }
    }
}
