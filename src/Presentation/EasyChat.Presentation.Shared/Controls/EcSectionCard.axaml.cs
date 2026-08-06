using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Metadata;
using Material.Icons;

namespace EasyChat.Presentation.Shared.Controls;

public partial class EcSectionCard : UserControl
{
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<EcSectionCard, string?>(nameof(Header));

    public static readonly StyledProperty<MaterialIconKind> HeaderIconProperty =
        AvaloniaProperty.Register<EcSectionCard, MaterialIconKind>(nameof(HeaderIcon), MaterialIconKind.CircleOutline);

    public static readonly StyledProperty<bool> ShowHeaderIconProperty =
        AvaloniaProperty.Register<EcSectionCard, bool>(nameof(ShowHeaderIcon), true);

    public static readonly StyledProperty<object?> BodyProperty =
        AvaloniaProperty.Register<EcSectionCard, object?>(nameof(Body));

    public EcSectionCard()
    {
        InitializeComponent();
    }

    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public MaterialIconKind HeaderIcon
    {
        get => GetValue(HeaderIconProperty);
        set => SetValue(HeaderIconProperty, value);
    }

    public bool ShowHeaderIcon
    {
        get => GetValue(ShowHeaderIconProperty);
        set => SetValue(ShowHeaderIconProperty, value);
    }

    [Content]
    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
