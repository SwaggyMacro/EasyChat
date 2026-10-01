using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EasyChat.Presentation.Shared.Controls;

public partial class KeySequenceDisplay : UserControl
{
    public static readonly StyledProperty<string?> KeyCombinationProperty =
        AvaloniaProperty.Register<KeySequenceDisplay, string?>(nameof(KeyCombination));

    public static readonly DirectProperty<KeySequenceDisplay, IEnumerable<string>> KeysProperty =
        AvaloniaProperty.RegisterDirect<KeySequenceDisplay, IEnumerable<string>>(
            nameof(Keys),
            o => o.Keys);

    private IEnumerable<string> _keys = [];

    public KeySequenceDisplay()
    {
        InitializeComponent();
    }

    public string? KeyCombination
    {
        get => GetValue(KeyCombinationProperty);
        set => SetValue(KeyCombinationProperty, value);
    }

    public IEnumerable<string> Keys
    {
        get => _keys;
        private set => SetAndRaise(KeysProperty, ref _keys, value);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == KeyCombinationProperty) UpdateKeys(change.NewValue as string);
    }

    private void UpdateKeys(string? combination)
    {
        if (string.IsNullOrEmpty(combination))
        {
            Keys = [];
            return;
        }

        // Split by '+' but trim spaces. Note: logic must match how we construct strings.
        // Usually "Ctrl + A" or "Ctrl+A"
        var parts = combination.Split('+')
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrEmpty(x))
            .Select(FormatKey);

        Keys = parts.ToList();
    }

    private static string FormatKey(string key) => KeyGlyphs.Format(key);
}
