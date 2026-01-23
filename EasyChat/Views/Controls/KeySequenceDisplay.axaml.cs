using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EasyChat.Views.Controls;

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

    private string FormatKey(string key)
    {
        // Handle digits D0-D9
        if (key.Length == 2 && key.StartsWith("D") && char.IsDigit(key[1])) return key[1].ToString();

        return key switch
        {
            "OemMinus" => "-",
            "OemPlus" => "+",
            "OemPeriod" => ".",
            "OemComma" => ",",
            "OemQuestion" => "?",
            "OemOpenBrackets" => "[",
            "OemCloseBrackets" => "]",
            "OemQuotes" => "\"",
            "OemSemicolon" => ";",
            "OemTilde" => "~",
            "OemPipe" => "|",
            "Oem1" => ";",
            "Oem2" => "/",
            "Oem3" => "`",
            "Oem4" => "[",
            "Oem5" => "\\",
            "Oem6" => "]",
            "Oem7" => "'",
            "Return" => "Enter",
            "Next" => "PgDn",
            "Prior" => "PgUp",
            "Back" => "Backspace",
            "Capital" => "Caps",
            _ => key
        };
    }
}