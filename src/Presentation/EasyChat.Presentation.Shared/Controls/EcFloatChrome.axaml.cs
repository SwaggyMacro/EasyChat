using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Metadata;
using Avalonia.Layout;

namespace EasyChat.Presentation.Shared.Controls;

public partial class EcFloatChrome : UserControl
{
    public static readonly StyledProperty<object?> BodyProperty =
        AvaloniaProperty.Register<EcFloatChrome, object?>(nameof(Body));

    public static readonly new StyledProperty<Thickness> PaddingProperty =
        AvaloniaProperty.Register<EcFloatChrome, Thickness>(nameof(Padding), new Thickness(12));

    public EcFloatChrome()
    {
        InitializeComponent();
    }

    [Content]
    public object? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    public new Thickness Padding
    {
        get => GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
