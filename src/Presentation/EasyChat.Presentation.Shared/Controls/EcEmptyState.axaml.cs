using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Material.Icons;

namespace EasyChat.Presentation.Shared.Controls;

public partial class EcEmptyState : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<EcEmptyState, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<EcEmptyState, string?>(nameof(Description));

    public static readonly StyledProperty<MaterialIconKind> IconProperty =
        AvaloniaProperty.Register<EcEmptyState, MaterialIconKind>(nameof(Icon), MaterialIconKind.InboxOutline);

    public static readonly StyledProperty<object?> ActionProperty =
        AvaloniaProperty.Register<EcEmptyState, object?>(nameof(Action));

    public EcEmptyState()
    {
        InitializeComponent();
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public MaterialIconKind Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public object? Action
    {
        get => GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
