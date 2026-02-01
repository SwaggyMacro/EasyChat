using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using EasyChat.Models.Translation.Selection;

namespace EasyChat.Controls;

/// <summary>
/// A high-performance control for displaying text tokens with interactive hover effects.
/// Uses direct rendering instead of creating individual controls for each token,
/// dramatically improving performance for large token counts (500+).
/// </summary>
public class VirtualizedTokenDisplay : Control
{
    #region Styled Properties

    public static readonly StyledProperty<IEnumerable<TextToken>?> TokensProperty =
        AvaloniaProperty.Register<VirtualizedTokenDisplay, IEnumerable<TextToken>?>(nameof(Tokens));

    public static readonly StyledProperty<ICommand?> WordClickedCommandProperty =
        AvaloniaProperty.Register<VirtualizedTokenDisplay, ICommand?>(nameof(WordClickedCommand));

    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<VirtualizedTokenDisplay, double>(nameof(FontSize), 15);

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<VirtualizedTokenDisplay, FontFamily>(nameof(FontFamily), FontFamily.Default);

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        AvaloniaProperty.Register<VirtualizedTokenDisplay, FontWeight>(nameof(FontWeight), FontWeight.Normal);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<VirtualizedTokenDisplay, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<IBrush?> NonWordForegroundProperty =
        AvaloniaProperty.Register<VirtualizedTokenDisplay, IBrush?>(nameof(NonWordForeground));

    public static readonly StyledProperty<IBrush?> UnderlineColorProperty =
        AvaloniaProperty.Register<VirtualizedTokenDisplay, IBrush?>(nameof(UnderlineColor));

    public static readonly StyledProperty<double> LineHeightProperty =
        AvaloniaProperty.Register<VirtualizedTokenDisplay, double>(nameof(LineHeight), 24);

    public static readonly StyledProperty<double> TokenSpacingProperty =
        AvaloniaProperty.Register<VirtualizedTokenDisplay, double>(nameof(TokenSpacing), 4);

    public static readonly StyledProperty<double> UnderlineHeightProperty =
        AvaloniaProperty.Register<VirtualizedTokenDisplay, double>(nameof(UnderlineHeight), 3);

    public IEnumerable<TextToken>? Tokens
    {
        get => GetValue(TokensProperty);
        set => SetValue(TokensProperty, value);
    }

    public ICommand? WordClickedCommand
    {
        get => GetValue(WordClickedCommandProperty);
        set => SetValue(WordClickedCommandProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush? NonWordForeground
    {
        get => GetValue(NonWordForegroundProperty);
        set => SetValue(NonWordForegroundProperty, value);
    }

    public IBrush? UnderlineColor
    {
        get => GetValue(UnderlineColorProperty);
        set => SetValue(UnderlineColorProperty, value);
    }

    public double LineHeight
    {
        get => GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    public double TokenSpacing
    {
        get => GetValue(TokenSpacingProperty);
        set => SetValue(TokenSpacingProperty, value);
    }

    public double UnderlineHeight
    {
        get => GetValue(UnderlineHeightProperty);
        set => SetValue(UnderlineHeightProperty, value);
    }

    #endregion

    #region Layout Cache

    private class TokenLayoutInfo
    {
        public TextToken Token { get; init; } = null!;
        public FormattedText FormattedText { get; init; } = null!;
        public Rect Bounds { get; set; }
        public int Index { get; init; }
    }

    private List<TokenLayoutInfo> _layoutCache = new();
    private Size _lastMeasuredSize;
    private bool _layoutDirty = true;

    #endregion

    #region Hover Animation State

    private int _hoveredTokenIndex = -1;
    private double _underlineScale;
    private DispatcherTimer? _animationTimer;
    private DateTime _animationStartTime;
    private const double AnimationDurationMs = 250;
    private static readonly CircularEaseOut EaseOut = new();

    #endregion

    static VirtualizedTokenDisplay()
    {
        AffectsRender<VirtualizedTokenDisplay>(
            TokensProperty, 
            ForegroundProperty,
            NonWordForegroundProperty,
            UnderlineColorProperty,
            FontSizeProperty,
            FontFamilyProperty,
            FontWeightProperty);

        AffectsMeasure<VirtualizedTokenDisplay>(
            TokensProperty,
            FontSizeProperty,
            FontFamilyProperty,
            FontWeightProperty,
            LineHeightProperty,
            TokenSpacingProperty);
    }

    public VirtualizedTokenDisplay()
    {
        ClipToBounds = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TokensProperty ||
            change.Property == FontSizeProperty ||
            change.Property == FontFamilyProperty ||
            change.Property == FontWeightProperty ||
            change.Property == LineHeightProperty ||
            change.Property == TokenSpacingProperty ||
            change.Property == ForegroundProperty ||
            change.Property == NonWordForegroundProperty)
        {
            _layoutDirty = true;
            InvalidateMeasure();
        }
    }

    #region Measure & Arrange

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_layoutDirty || Math.Abs(_lastMeasuredSize.Width - availableSize.Width) > 0.1)
        {
            RecalculateLayout(availableSize.Width);
            _lastMeasuredSize = availableSize;
            _layoutDirty = false;
        }

        if (_layoutCache.Count == 0)
            return new Size(0, LineHeight);

        var lastToken = _layoutCache[^1];
        var totalHeight = lastToken.Bounds.Bottom + 3; // Add some padding at bottom
        
        return new Size(availableSize.Width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Math.Abs(_lastMeasuredSize.Width - finalSize.Width) > 0.1)
        {
            RecalculateLayout(finalSize.Width);
            _lastMeasuredSize = finalSize;
        }

        return base.ArrangeOverride(finalSize);
    }

    private void RecalculateLayout(double availableWidth)
    {
        _layoutCache.Clear();

        var tokens = Tokens?.ToList();
        if (tokens == null || tokens.Count == 0)
            return;

        var foreground = Foreground ?? Brushes.Black;
        var nonWordForeground = NonWordForeground ?? Brushes.Gray;
        var typeface = new Typeface(FontFamily, FontStyle.Normal, FontWeight);
        var fontSize = FontSize;
        var lineHeight = LineHeight;
        var spacing = TokenSpacing;

        double x = 0;
        double y = 0;
        int index = 0;

        foreach (var token in tokens)
        {
            // Use gray color for non-word tokens (punctuation, spaces)
            var tokenBrush = token.IsWord ? foreground : nonWordForeground;
            
            var formattedText = new FormattedText(
                token.Text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                tokenBrush);

            var tokenWidth = formattedText.Width;
            var tokenHeight = formattedText.Height;

            // Wrap to next line if needed
            if (x + tokenWidth > availableWidth && x > 0)
            {
                x = 0;
                y += lineHeight;
            }

            var bounds = new Rect(x, y, tokenWidth, tokenHeight);

            _layoutCache.Add(new TokenLayoutInfo
            {
                Token = token,
                FormattedText = formattedText,
                Bounds = bounds,
                Index = index
            });

            x += tokenWidth + spacing;
            index++;
        }
    }

    #endregion

    #region Rendering

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_layoutCache.Count == 0)
            return;

        var underlineBrush = UnderlineColor ?? Brushes.Blue;
        var underlineHeight = UnderlineHeight;

        foreach (var layoutInfo in _layoutCache)
        {
            // Draw token text
            context.DrawText(layoutInfo.FormattedText, layoutInfo.Bounds.TopLeft);

            // Draw underline for hovered word token
            if (layoutInfo.Index == _hoveredTokenIndex && 
                layoutInfo.Token.IsWord && 
                _underlineScale > 0)
            {
                var underlineWidth = layoutInfo.Bounds.Width * _underlineScale;
                var underlineY = layoutInfo.Bounds.Bottom;
                
                context.FillRectangle(
                    underlineBrush,
                    new Rect(
                        layoutInfo.Bounds.Left,
                        underlineY,
                        underlineWidth,
                        underlineHeight));
            }
        }
    }

    #endregion

    #region Hit Testing & Interaction

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var position = e.GetPosition(this);
        var hitIndex = HitTestToken(position);

        // Only process word tokens for hover effect
        if (hitIndex >= 0 && !_layoutCache[hitIndex].Token.IsWord)
        {
            hitIndex = -1;
        }

        if (hitIndex != _hoveredTokenIndex)
        {
            _hoveredTokenIndex = hitIndex;

            if (hitIndex >= 0)
            {
                // Start animation for new hovered token
                StartUnderlineAnimation(true);
                Cursor = new Cursor(StandardCursorType.Hand);
            }
            else
            {
                // Start fade-out animation
                StartUnderlineAnimation(false);
                Cursor = Cursor.Default;
            }
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_hoveredTokenIndex >= 0)
        {
            _hoveredTokenIndex = -1;
            StartUnderlineAnimation(false);
            Cursor = Cursor.Default;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var position = e.GetPosition(this);
            var hitIndex = HitTestToken(position);

            if (hitIndex >= 0 && _layoutCache[hitIndex].Token.IsWord)
            {
                var command = WordClickedCommand;
                var word = _layoutCache[hitIndex].Token.Text;

                if (command?.CanExecute(word) == true)
                {
                    command.Execute(word);
                    e.Handled = true;
                }
            }
        }
    }

    private int HitTestToken(Point position)
    {
        for (int i = 0; i < _layoutCache.Count; i++)
        {
            if (_layoutCache[i].Bounds.Contains(position))
            {
                return i;
            }
        }
        return -1;
    }

    #endregion

    #region Animation

    private void StartUnderlineAnimation(bool show)
    {
        _animationStartTime = DateTime.Now;
        
        if (_animationTimer == null)
        {
            _animationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
            };
            _animationTimer.Tick += OnAnimationTick;
        }

        if (show)
        {
            _animationTimer.Start();
        }
        else
        {
            // For fade-out, keep the current hovered index until animation completes
            _animationTimer.Start();
        }
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        var elapsed = (DateTime.Now - _animationStartTime).TotalMilliseconds;
        var progress = Math.Min(1.0, elapsed / AnimationDurationMs);
        var easedProgress = EaseOut.Ease(progress);

        if (_hoveredTokenIndex >= 0)
        {
            // Animate in
            _underlineScale = easedProgress;
        }
        else
        {
            // Animate out
            _underlineScale = 1.0 - easedProgress;
        }

        InvalidateVisual();

        if (progress >= 1.0)
        {
            _animationTimer?.Stop();
            
            if (_hoveredTokenIndex < 0)
            {
                _underlineScale = 0;
            }
        }
    }

    #endregion
}
