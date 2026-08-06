using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace EasyChat.Presentation.Shared.Controls;

public enum EcStatusKind
{
    Neutral,
    Success,
    Warning,
    Danger
}

public partial class EcStatusPill : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<EcStatusPill, string?>(nameof(Text));

    public static readonly StyledProperty<EcStatusKind> KindProperty =
        AvaloniaProperty.Register<EcStatusPill, EcStatusKind>(nameof(Kind));

    public static readonly StyledProperty<IBrush> DotBrushProperty =
        AvaloniaProperty.Register<EcStatusPill, IBrush>(nameof(DotBrush), Brushes.Gray);

    public EcStatusPill()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            ActualThemeVariantChanged += OnThemeVariantChanged;
            UpdateVisual();
        };
        DetachedFromVisualTree += (_, _) => ActualThemeVariantChanged -= OnThemeVariantChanged;
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public EcStatusKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush DotBrush
    {
        get => GetValue(DotBrushProperty);
        private set => SetValue(DotBrushProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty || change.Property == TextProperty)
            UpdateVisual();
    }

    private void OnThemeVariantChanged(object? sender, EventArgs e) => UpdateVisual();

    private void UpdateVisual()
    {
        if (this.FindControl<Border>("PillBorder") is not { } border)
            return;

        var label = string.IsNullOrWhiteSpace(Text) ? Kind.ToString() : Text!;
        AutomationProperties.SetName(this, label);
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Content);

        var (bg, edge, fg, dot) = Kind switch
        {
            EcStatusKind.Success => ("EcStatusSuccessBg", "EcStatusSuccessBorder", "EcStatusSuccessFg", "EcStatusSuccessDot"),
            EcStatusKind.Warning => ("EcStatusWarningBg", "EcStatusWarningBorder", "EcStatusWarningFg", "EcStatusWarningDot"),
            EcStatusKind.Danger => ("EcStatusDangerBg", "EcStatusDangerBorder", "EcStatusDangerFg", "EcStatusDangerDot"),
            _ => ("EcStatusNeutralBg", "EcStatusNeutralBorder", "EcStatusNeutralFg", "EcStatusNeutralDot")
        };

        border.Background = ResolveBrush(bg, Color.Parse("#D1FAE5"));
        border.BorderBrush = ResolveBrush(edge, Color.Parse("#6EE7B7"));
        Foreground = ResolveBrush(fg, Color.Parse("#047857"));
        DotBrush = ResolveBrush(dot, Color.Parse("#059669"));
    }

    private IBrush ResolveBrush(string key, Color fallback)
    {
        if (this.TryGetResource(key, ActualThemeVariant, out var value) && value is IBrush brush)
            return brush;
        return new SolidColorBrush(fallback);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
