using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons;

namespace EasyChat.Presentation.Shared.Controls;

/// <summary>
/// Keeps the autocomplete popup compact while allowing visible long items to be
/// read without introducing a horizontal scrollbar.
/// </summary>
public sealed class LanguageAutoCompleteBox : AutoCompleteBox
{
    private const string PopupPartName = "PART_Popup";
    private const double ArrowButtonWidth = 32;
    private const double PopupEdgeInset = 8;
    private const double PopupScrollBarRightInset = 2;
    private const double FallbackPopupHorizontalChrome = 94;
    private const double TextWidthSafetyMargin = 4;
    private const double MaximumPopupWidth = 540;
    private const int MaximumWidthUpdateRetries = 8;
    private const double MinimumMeasuredPopupChrome = 48;
    private const double MaximumMeasuredPopupChrome = 140;

    private Popup? _dropDownPopup;
    private ScrollViewer? _dropDownScrollViewer;
    private Control? _popupClipTarget;
    private CornerRadius _popupCornerRadius;
    private double _basePopupWidth;
    private double _popupWidth;
    private string _allItemsSearchText = string.Empty;
    private string? _textBeforeOpening;
    private bool _showAllItems;
    private bool _restoreTextWhenClosing;
    private bool _widthUpdateQueued;
    private bool _isUpdatingPopupWidth;
    private int _widthUpdateRetries;
    private TopLevel? _topLevel;
    private Window? _window;
    private TextBox? _attachedTextBox;
    private bool _allowSelectAll;
    private bool _adjustingSelection;

    public static readonly StyledProperty<MaterialIconKind> DropDownIconProperty =
        AvaloniaProperty.Register<LanguageAutoCompleteBox, MaterialIconKind>(
            nameof(DropDownIcon),
            MaterialIconKind.ChevronDown);

    protected override Type StyleKeyOverride => typeof(AutoCompleteBox);

    public MaterialIconKind DropDownIcon
    {
        get => GetValue(DropDownIconProperty);
        private set => SetValue(DropDownIconProperty, value);
    }

    public LanguageAutoCompleteBox()
    {
        DropDownOpened += OnDropDownOpened;
        DropDownClosed += OnDropDownClosed;
        Populated += OnPopulated;
        LostFocus += OnSelectorLostFocus;
        PointerPressed += OnSelectorPointerPressed;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        DetachPopupClip();
        DetachDropDownScrollViewer();
        DetachDropDownPopup();
        DetachTextBox();
        base.OnApplyTemplate(e);
        TextFilter = FilterText;
        _dropDownPopup = e.NameScope.Find<Popup>(PopupPartName);
        if (_dropDownPopup is not null)
        {
            _dropDownPopup.Placement = PlacementMode.BottomEdgeAlignedRight;
            AttachDropDownPopup(_dropDownPopup);
        }
        AttachTextBox(e.NameScope.Find<TextBox>("PART_TextBox"));
        _basePopupWidth = Bounds.Width;
        _popupWidth = _basePopupWidth;
        ApplyPopupWidth();

        if (IsDropDownOpen)
            QueuePopupWidthUpdate();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arrangedSize = base.ArrangeOverride(finalSize);

        if (arrangedSize.Width > 0)
        {
            _basePopupWidth = arrangedSize.Width;
            if (!IsDropDownOpen || _popupWidth < _basePopupWidth)
                _popupWidth = _basePopupWidth;
            ApplyPopupWidth();
        }

        return arrangedSize;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);

        PrepareToShowAllItems();

        // base.OnGotFocus -> FocusChanged calls TextBox.SelectAll() whenever the
        // control regains focus without a selection (e.g. after picking an item,
        // which refocuses the text box). Restoring the caret right after the
        // base call, in the same event dispatch, leaves the final selection
        // state correct - no all-selected frame is ever rendered. This override
        // guarantees ordering, unlike a plain GotFocus instance handler.
        RestoreCaretToEnd();
    }

    private void RestoreCaretToEnd()
    {
        var textBox = _attachedTextBox
            ?? this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        if (textBox is null)
            return;

        var selectionLength = Math.Abs(textBox.SelectionEnd - textBox.SelectionStart);
        if (selectionLength > 1)
            return; // user-selected text (e.g. Ctrl+A) must be preserved

        var length = textBox.Text?.Length ?? 0;
        textBox.CaretIndex = length;
        textBox.SelectionStart = length;
        textBox.SelectionEnd = length;
    }

    private void AttachTextBox(TextBox? textBox)
    {
        _attachedTextBox = textBox;
        if (_attachedTextBox is null)
            return;

        _attachedTextBox.KeyDown += OnTextBoxKeyDown;
        _attachedTextBox.KeyUp += OnTextBoxKeyUp;
        _attachedTextBox.PropertyChanged += OnTextBoxPropertyChanged;
    }

    private void DetachTextBox()
    {
        if (_attachedTextBox is null)
            return;

        _attachedTextBox.KeyDown -= OnTextBoxKeyDown;
        _attachedTextBox.KeyUp -= OnTextBoxKeyUp;
        _attachedTextBox.PropertyChanged -= OnTextBoxPropertyChanged;
        _attachedTextBox = null;
    }

    private void OnTextBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty)
            OnTextBoxSelectionChanged();
    }

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.A)
            _allowSelectAll = true;
    }

    private void OnTextBoxKeyUp(object? sender, KeyEventArgs e)
    {
        if (_allowSelectAll && e.Key == Key.A)
            _allowSelectAll = false;
    }

    private void OnTextBoxSelectionChanged()
    {
        // AutoCompleteBox's built-in FocusChanged handler (and any other source)
        // calls TextBox.SelectAll() asynchronously after the focus event, so
        // restoring the caret inside OnGotFocus is too early. Intercept the
        // selection here instead: whenever the whole text becomes selected not
        // by the user (Ctrl+A), synchronously move the caret to the end - same
        // event, no intermediate render, so no flicker and no persistent
        // all-selected state.
        if (_allowSelectAll || _adjustingSelection || _attachedTextBox is null)
            return;

        var length = _attachedTextBox.Text?.Length ?? 0;
        if (length <= 0)
            return;

        if (_attachedTextBox.SelectionStart != 0 || _attachedTextBox.SelectionEnd != length)
            return;

        _adjustingSelection = true;
        try
        {
            _attachedTextBox.CaretIndex = length;
            _attachedTextBox.SelectionStart = length;
            _attachedTextBox.SelectionEnd = length;
        }
        finally
        {
            _adjustingSelection = false;
        }
    }

    private void OnSelectorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (IsDropDownOpen && e.GetPosition(this).X >= Bounds.Width - ArrowButtonWidth)
        {
            CloseDropDownAndRestoreText();
            e.Handled = true;
            return;
        }

        PrepareToShowAllItems();
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsDropDownOpen)
                OpenDropDownWithAllItems();
        }, DispatcherPriority.Input);
    }

    private void OnSelectorLostFocus(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsKeyboardFocusWithin)
                CloseDropDownAndRestoreText();
        }, DispatcherPriority.Background);
    }

    private void OpenDropDownWithAllItems()
    {
        PrepareToShowAllItems();
        IsDropDownOpen = true;
    }

    public void ToggleDropDown()
    {
        Focus();

        if (IsDropDownOpen)
        {
            CloseDropDownAndRestoreText();
            return;
        }

        OpenDropDownWithAllItems();
    }

    private void PrepareToShowAllItems()
    {
        if (!IsDropDownOpen)
            _textBeforeOpening = Text;

        _allItemsSearchText = Text ?? string.Empty;
        _showAllItems = true;
    }

    private void CloseDropDownAndRestoreText()
    {
        if (IsDropDownOpen)
        {
            _restoreTextWhenClosing = true;
            IsDropDownOpen = false;
            return;
        }

        RestoreTextBeforeOpening();
    }

    private void RestoreTextBeforeOpening()
    {
        if (_textBeforeOpening is not null)
            Text = _textBeforeOpening;
    }

    private void OnDropDownOpened(object? sender, EventArgs e)
    {
        _textBeforeOpening ??= Text;
        DropDownIcon = MaterialIconKind.ChevronUp;
        _widthUpdateRetries = 0;
        AttachDropDownPopup(_dropDownPopup);
        QueuePopupWidthUpdate();
        AttachTopLevelDismissal();
    }

    private void OnDropDownClosed(object? sender, EventArgs e)
    {
        DetachDropDownScrollViewer();
        DetachTopLevelDismissal();
        _showAllItems = false;
        if (_restoreTextWhenClosing)
            RestoreTextBeforeOpening();
        _restoreTextWhenClosing = false;
        _textBeforeOpening = Text;
        DropDownIcon = MaterialIconKind.ChevronDown;
        _widthUpdateRetries = 0;
        _popupWidth = _basePopupWidth;
        ApplyPopupWidth();
    }

    private void OnPopulated(object? sender, PopulatedEventArgs e)
    {
        if (IsDropDownOpen)
            QueuePopupWidthUpdate();
    }

    private void AttachDropDownPopup(Popup? popup)
    {
        if (popup is null)
            return;

        popup.Opened -= OnPopupOpened;
        popup.Opened += OnPopupOpened;
        popup.Closed -= OnPopupClosed;
        popup.Closed += OnPopupClosed;
        popup.PropertyChanged -= OnPopupPropertyChanged;
        popup.PropertyChanged += OnPopupPropertyChanged;
    }

    private void DetachDropDownPopup()
    {
        if (_dropDownPopup is null)
            return;

        _dropDownPopup.Opened -= OnPopupOpened;
        _dropDownPopup.Closed -= OnPopupClosed;
        _dropDownPopup.PropertyChanged -= OnPopupPropertyChanged;
        _dropDownPopup = null;
    }

    private void OnPopupOpened(object? sender, EventArgs e)
    {
        if (IsDropDownOpen)
            QueuePopupWidthUpdate();
    }

    private void OnPopupClosed(object? sender, EventArgs e) =>
        DetachDropDownScrollViewer();

    private void OnPopupPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Popup.ChildProperty)
            return;

        DetachDropDownScrollViewer();
        if (IsDropDownOpen)
            QueuePopupWidthUpdate();
    }

    private void AttachTopLevelDismissal()
    {
        if (_topLevel is not null)
            return;

        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is null)
            return;

        _topLevel.PointerPressed += OnTopLevelPointerPressed;

        if (_topLevel is Window window)
        {
            _window = window;
            _window.Deactivated += OnTopLevelDeactivated;
        }
    }

    private void DetachTopLevelDismissal()
    {
        if (_topLevel is null)
            return;

        _topLevel.PointerPressed -= OnTopLevelPointerPressed;
        _topLevel = null;

        if (_window is not null)
        {
            _window.Deactivated -= OnTopLevelDeactivated;
            _window = null;
        }
    }

    private void OnTopLevelDeactivated(object? sender, EventArgs e) =>
        CloseDropDownAndRestoreText();

    private void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsDropDownOpen)
            return;

        // Clicks inside the selector itself (or the drop-down arrow overlaid on
        // top of it) keep the popup open; clicking anywhere else dismisses it.
        var position = e.GetPosition(this);
        if (position.X >= 0
            && position.Y >= 0
            && position.X <= Bounds.Width
            && position.Y <= Bounds.Height)
        {
            return;
        }

        if (_dropDownPopup?.Child is { } popupChild
            && popupChild.Bounds.Width > 0
            && popupChild.Bounds.Height > 0)
        {
            var popupPosition = e.GetPosition(popupChild);
            if (popupPosition.X >= 0
                && popupPosition.Y >= 0
                && popupPosition.X <= popupChild.Bounds.Width
                && popupPosition.Y <= popupChild.Bounds.Height)
            {
                return;
            }
        }

        CloseDropDownAndRestoreText();
    }

    private void OnDropDownScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // Changing the popup width can invalidate the ScrollViewer viewport and
        // raise ScrollChanged itself. Ignore horizontal viewport changes, which
        // are generated by our own width update, while still reacting to a real
        // vertical scroll or a newly realized vertical extent.
        if (_isUpdatingPopupWidth)
            return;

        var verticalOffsetChanged = Math.Abs(e.OffsetDelta.Y) >= 0.01;
        var verticalExtentChanged = Math.Abs(e.ExtentDelta.Y) >= 0.01;
        if (!verticalOffsetChanged && !verticalExtentChanged)
            return;

        if (verticalExtentChanged)
            ConfigurePopupItems(_dropDownScrollViewer!);

        QueuePopupWidthUpdate();
    }

    private bool FilterText(string? searchText, string? itemText)
    {
        searchText ??= string.Empty;

        // Keep the selected value visible while the arrow opens the complete
        // list. A different search string means the user has started typing.
        if (_showAllItems)
        {
            if (string.Equals(searchText, _allItemsSearchText, StringComparison.Ordinal))
                return true;

            _showAllItems = false;
        }

        return itemText?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true;
    }

    private void QueuePopupWidthUpdate()
    {
        if (_widthUpdateQueued)
            return;

        _widthUpdateQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _widthUpdateQueued = false;
            if (_isUpdatingPopupWidth || !IsDropDownOpen)
                return;

            AttachDropDownScrollViewer();
            if (_dropDownScrollViewer is null || _dropDownScrollViewer.Bounds.Height <= 0)
            {
                if (++_widthUpdateRetries <= MaximumWidthUpdateRetries)
                    QueuePopupWidthUpdate();
                return;
            }

            if (UpdatePopupWidthFromVisibleItems())
            {
                _widthUpdateRetries = 0;
            }
            else if (++_widthUpdateRetries <= MaximumWidthUpdateRetries)
            {
                QueuePopupWidthUpdate();
            }
        }, DispatcherPriority.Render);
    }

    private void AttachDropDownScrollViewer()
    {
        var scrollViewer = _dropDownPopup?.Child as ScrollViewer
            ?? _dropDownPopup?.Child?
                .GetVisualDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault();

        if (scrollViewer is null)
        {
            DetachDropDownScrollViewer();
            return;
        }

        if (ReferenceEquals(scrollViewer, _dropDownScrollViewer))
        {
            ConfigurePopupItems(scrollViewer);
            ConfigurePopupScrollBars(scrollViewer);
            return;
        }

        DetachDropDownScrollViewer();
        _dropDownScrollViewer = scrollViewer;
        _dropDownScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
        _dropDownScrollViewer.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        _dropDownScrollViewer.ScrollChanged += OnDropDownScrollChanged;
        _dropDownScrollViewer.SizeChanged += OnDropDownScrollViewerSizeChanged;

        if (_dropDownPopup?.Child is { } child)
        {
            child.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            child.ClipToBounds = true;
            ConfigurePopupChrome(child);
        }

        ConfigurePopupItems(_dropDownScrollViewer);
        ConfigurePopupScrollBars(_dropDownScrollViewer);
    }

    private void DetachDropDownScrollViewer()
    {
        if (_dropDownScrollViewer is null)
            return;

        _dropDownScrollViewer.ScrollChanged -= OnDropDownScrollChanged;
        _dropDownScrollViewer.SizeChanged -= OnDropDownScrollViewerSizeChanged;
        _dropDownScrollViewer = null;
    }

    private void OnDropDownScrollViewerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_dropDownScrollViewer is null || !IsDropDownOpen || _isUpdatingPopupWidth)
            return;

        // Viewport width changes can leave the realized ListBoxItems with the
        // previous MaxWidth. Reapply the item constraints after the new layout
        // so a popup widened from a narrow selector also widens its text column.
        ConfigurePopupItems(_dropDownScrollViewer);
        ConfigurePopupScrollBars(_dropDownScrollViewer);
        QueuePopupWidthUpdate();
    }

    private bool UpdatePopupWidthFromVisibleItems()
    {
        if (_dropDownScrollViewer is null || _dropDownScrollViewer.Bounds.Height <= 0)
            return false;

        _isUpdatingPopupWidth = true;
        try
        {
            var maxTextWidth = 0d;
            var chromeCandidates = new List<double>();
            var viewportHeight = _dropDownScrollViewer.Bounds.Height;
            var popupChild = _dropDownPopup?.Child;
            var currentPopupWidth = popupChild?.Bounds.Width > 0
                ? popupChild.Bounds.Width
                : _popupWidth;

            foreach (var textBlock in _dropDownScrollViewer.GetVisualDescendants().OfType<TextBlock>())
            {
                if (string.IsNullOrWhiteSpace(textBlock.Text))
                    continue;

                var point = textBlock.TranslatePoint(new Point(0, 0), _dropDownScrollViewer);
                if (point is null
                    || point.Value.Y + textBlock.Bounds.Height < 0
                    || point.Value.Y > viewportHeight)
                {
                    continue;
                }

                var measureBlock = new TextBlock
                {
                    Text = textBlock.Text,
                    FontFamily = textBlock.FontFamily,
                    FontSize = textBlock.FontSize,
                    FontWeight = textBlock.FontWeight,
                    FontStyle = textBlock.FontStyle,
                    FontStretch = textBlock.FontStretch,
                    LetterSpacing = textBlock.LetterSpacing,
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.None
                };
                measureBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var desiredTextWidth = measureBlock.DesiredSize.Width;
                maxTextWidth = Math.Max(maxTextWidth, desiredTextWidth);

                if (currentPopupWidth > 0 && textBlock.Bounds.Width > 0)
                {
                    // Estimate the non-text portion from each realized item, but
                    // keep only plausible values. A stale layout pass can report
                    // a tiny text width; allowing that value into the calculation
                    // would make the popup jump straight to its maximum width.
                    var measuredChrome = currentPopupWidth
                        - textBlock.Bounds.Width
                        + TextWidthSafetyMargin;
                    if (measuredChrome >= MinimumMeasuredPopupChrome
                        && measuredChrome <= MaximumMeasuredPopupChrome)
                        chromeCandidates.Add(measuredChrome);
                }
            }

            if (maxTextWidth <= 0)
                return false;

            var popupHorizontalChrome = FallbackPopupHorizontalChrome;
            if (chromeCandidates.Count > 0)
            {
                chromeCandidates.Sort();
                var middle = chromeCandidates.Count / 2;
                popupHorizontalChrome = chromeCandidates.Count % 2 == 0
                    ? (chromeCandidates[middle - 1] + chromeCandidates[middle]) / 2
                    : chromeCandidates[middle];
            }

            var minimumPopupWidth = _basePopupWidth + PopupEdgeInset * 2;
            var maximumPopupWidth = Math.Max(MaximumPopupWidth, minimumPopupWidth);
            var desiredWidth = Math.Clamp(
                maxTextWidth + popupHorizontalChrome,
                minimumPopupWidth,
                maximumPopupWidth);

            if (Math.Abs(_popupWidth - desiredWidth) < 0.5)
                return true;

            _popupWidth = desiredWidth;
            ApplyPopupWidth();
            return true;
        }
        finally
        {
            _isUpdatingPopupWidth = false;
        }
    }

    private void ApplyPopupWidth()
    {
        if (_dropDownPopup is null || double.IsNaN(_popupWidth) || _popupWidth <= 0)
            return;

        _dropDownPopup.Width = _popupWidth;

        if (_dropDownPopup.Child is { } child)
        {
            child.Width = _popupWidth;
            child.MaxWidth = _popupWidth;
            child.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            child.ClipToBounds = true;
            ConfigurePopupChrome(child);

            if (_dropDownScrollViewer is not null)
            {
                ConfigurePopupItems(_dropDownScrollViewer);
                ConfigurePopupScrollBars(_dropDownScrollViewer);
            }

        }
    }

    private void ConfigurePopupChrome(Control popupChild)
    {
        var roundedBorder = popupChild as Border;
        if (roundedBorder is not null && !HasCornerRadius(roundedBorder))
        {
            roundedBorder = popupChild.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(HasCornerRadius);
        }

        if (roundedBorder is not null)
        {
            ConfigurePopupBorder(roundedBorder);
            EnsurePopupEdgeInset(roundedBorder);
        }

        foreach (var border in popupChild.GetVisualDescendants().OfType<Border>())
            ConfigurePopupBorder(border);

        AttachPopupClip(
            popupChild,
            roundedBorder is not null ? roundedBorder.CornerRadius : new CornerRadius(8));
    }

    private static void EnsurePopupEdgeInset(Border popupBorder)
    {
        // Reserve a small gutter so the scrollbar cannot cover the popup's rounded edge.
        var padding = popupBorder.Padding;
        popupBorder.Padding = new Thickness(
            Math.Max(padding.Left, PopupEdgeInset),
            Math.Max(padding.Top, PopupEdgeInset),
            PopupScrollBarRightInset,
            Math.Max(padding.Bottom, PopupEdgeInset));
    }

    private static void ConfigurePopupScrollBars(ScrollViewer scrollViewer)
    {
        foreach (var scrollBar in scrollViewer.GetVisualDescendants().OfType<ScrollBar>())
        {
            if (scrollBar.Orientation != Avalonia.Layout.Orientation.Vertical)
                continue;

            var margin = scrollBar.Margin;
            scrollBar.Margin = new Thickness(
                margin.Left,
                Math.Max(margin.Top, PopupEdgeInset),
                PopupScrollBarRightInset,
                Math.Max(margin.Bottom, PopupEdgeInset));
        }
    }

    private void AttachPopupClip(Control target, CornerRadius radius)
    {
        if (!ReferenceEquals(target, _popupClipTarget))
        {
            DetachPopupClip();
            _popupClipTarget = target;
            _popupClipTarget.SizeChanged += OnPopupClipTargetSizeChanged;
        }

        _popupCornerRadius = radius;
        UpdatePopupClip();
    }

    private void DetachPopupClip()
    {
        if (_popupClipTarget is null)
            return;

        _popupClipTarget.SizeChanged -= OnPopupClipTargetSizeChanged;
        _popupClipTarget.Clip = null;
        _popupClipTarget = null;
    }

    private void OnPopupClipTargetSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdatePopupClip();

        if (IsDropDownOpen && !_isUpdatingPopupWidth)
            QueuePopupWidthUpdate();
    }

    private void UpdatePopupClip()
    {
        if (_popupClipTarget is null
            || _popupClipTarget.Bounds.Width <= 0
            || _popupClipTarget.Bounds.Height <= 0)
        {
            return;
        }

        if (!HasCornerRadius(_popupCornerRadius))
        {
            _popupClipTarget.Clip = null;
            return;
        }

        _popupClipTarget.Clip = CreateRoundedClip(
            new Size(_popupClipTarget.Bounds.Width, _popupClipTarget.Bounds.Height),
            _popupCornerRadius);
    }

    private static StreamGeometry CreateRoundedClip(Size size, CornerRadius corners)
    {
        var maxRadius = Math.Min(size.Width, size.Height) / 2;
        var topLeft = Math.Clamp(corners.TopLeft, 0, maxRadius);
        var topRight = Math.Clamp(corners.TopRight, 0, maxRadius);
        var bottomRight = Math.Clamp(corners.BottomRight, 0, maxRadius);
        var bottomLeft = Math.Clamp(corners.BottomLeft, 0, maxRadius);
        var geometry = new StreamGeometry();

        using var context = geometry.Open();
        context.BeginFigure(new Point(topLeft, 0), isFilled: true);
        context.LineTo(new Point(size.Width - topRight, 0));
        if (topRight > 0)
            context.ArcTo(
                new Point(size.Width, topRight),
                new Size(topRight, topRight),
                rotationAngle: 0,
                isLargeArc: false,
                SweepDirection.Clockwise);
        context.LineTo(new Point(size.Width, size.Height - bottomRight));
        if (bottomRight > 0)
            context.ArcTo(
                new Point(size.Width - bottomRight, size.Height),
                new Size(bottomRight, bottomRight),
                rotationAngle: 0,
                isLargeArc: false,
                SweepDirection.Clockwise);
        context.LineTo(new Point(bottomLeft, size.Height));
        if (bottomLeft > 0)
            context.ArcTo(
                new Point(0, size.Height - bottomLeft),
                new Size(bottomLeft, bottomLeft),
                rotationAngle: 0,
                isLargeArc: false,
                SweepDirection.Clockwise);
        context.LineTo(new Point(0, topLeft));
        if (topLeft > 0)
            context.ArcTo(
                new Point(topLeft, 0),
                new Size(topLeft, topLeft),
                rotationAngle: 0,
                isLargeArc: false,
                SweepDirection.Clockwise);
        context.EndFigure(isClosed: true);
        return geometry;
    }

    private static bool HasCornerRadius(CornerRadius corners) =>
        corners.TopLeft > 0
        || corners.TopRight > 0
        || corners.BottomRight > 0
        || corners.BottomLeft > 0;

    private static bool HasCornerRadius(Border border) => HasCornerRadius(border.CornerRadius);

    private static void ConfigurePopupBorder(Border border)
    {
        var radius = border.CornerRadius;
        if (radius.TopLeft <= 0
            && radius.TopRight <= 0
            && radius.BottomRight <= 0
            && radius.BottomLeft <= 0)
        {
            return;
        }

        border.ClipToBounds = true;
        if (border.Child is Panel panel)
            panel.Background = null;
    }

    private static void ConfigurePopupItems(Control popupRoot)
    {
        var scrollViewer = popupRoot as ScrollViewer
            ?? popupRoot.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var viewportWidth = scrollViewer?.Viewport.Width > 0
            ? scrollViewer.Viewport.Width
            : scrollViewer?.Bounds.Width ?? 0;
        var itemMaxWidth = viewportWidth > 0
            ? Math.Max(0, viewportWidth - PopupEdgeInset)
            : double.PositiveInfinity;

        foreach (var listItem in popupRoot.GetVisualDescendants().OfType<ListBoxItem>())
            ConfigurePopupItem(listItem, itemMaxWidth);
    }

    private static void ConfigurePopupItem(ListBoxItem listItem, double itemMaxWidth)
    {
        listItem.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        listItem.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        listItem.Margin = new Thickness(0, 0, PopupEdgeInset, 0);
        listItem.Width = double.NaN;
        listItem.MaxWidth = itemMaxWidth;
        listItem.Padding = new Thickness(0);
        listItem.CornerRadius = new CornerRadius(4);
        listItem.ClipToBounds = true;
    }
}
