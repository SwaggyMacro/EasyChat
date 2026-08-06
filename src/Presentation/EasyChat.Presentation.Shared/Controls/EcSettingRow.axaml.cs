using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace EasyChat.Presentation.Shared.Controls;

/// <summary>
/// Settings row: label + optional 28px help hit on the left, value control on the right.
/// Helper text is pushed into the popup TextBlock in code so it survives popup reparenting.
/// </summary>
public class EcSettingRow : ContentControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<EcSettingRow, string?>(nameof(Label));

    public static readonly StyledProperty<string?> HelperProperty =
        AvaloniaProperty.Register<EcSettingRow, string?>(nameof(Helper));

    private Border? _helpHit;
    private Popup? _helperPopup;
    private TextBlock? _helperText;
    private bool _pointerInside;

    protected override Type StyleKeyOverride => typeof(EcSettingRow);

    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? Helper
    {
        get => GetValue(HelperProperty);
        set => SetValue(HelperProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_helpHit is not null)
        {
            _helpHit.PointerEntered -= OnHelpPointerEntered;
            _helpHit.PointerExited -= OnHelpPointerExited;
        }

        base.OnApplyTemplate(e);

        _helpHit = e.NameScope.Find<Border>("PART_HelpHit");
        _helperPopup = e.NameScope.Find<Popup>("PART_HelperPopup");
        _helperText = e.NameScope.Find<TextBlock>("PART_HelperText");

        if (_helperPopup is not null && _helpHit is not null)
            _helperPopup.PlacementTarget = _helpHit;

        if (_helperText is not null)
            _helperText.Text = Helper ?? string.Empty;

        if (_helpHit is null)
            return;

        _helpHit.PointerEntered += OnHelpPointerEntered;
        _helpHit.PointerExited += OnHelpPointerExited;
        SyncHelperOpen();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != HelperProperty)
            return;

        if (_helperText is not null)
            _helperText.Text = Helper ?? string.Empty;
        SyncHelperOpen();
    }

    private void OnHelpPointerEntered(object? sender, PointerEventArgs e)
    {
        _pointerInside = true;
        SyncHelperOpen();
    }

    private void OnHelpPointerExited(object? sender, PointerEventArgs e)
    {
        _pointerInside = false;
        SyncHelperOpen();
    }

    private void SyncHelperOpen()
    {
        var open = _pointerInside && !string.IsNullOrWhiteSpace(Helper);
        if (_helperPopup is null || _helperPopup.IsOpen == open)
            return;

        if (open && _helperText is not null)
            _helperText.Text = Helper ?? string.Empty;

        _helperPopup.IsOpen = open;
    }
}
