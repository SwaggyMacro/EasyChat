using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections;

namespace EasyChat.Presentation.Shared.Controls;

/// <summary>
/// A generic language picker: auto-complete box with the selected language's
/// flag on the left and a drop-down arrow button on the right. Items only need
/// a <c>DisplayName</c> (text shown in the box / filtered on) and an
/// <c>Icon</c> (flag asset file name) — see <see cref="LanguageFlagConverters"/>.
/// </summary>
public sealed partial class LanguageSelector : UserControl
{
    public static readonly StyledProperty<IEnumerable> LanguagesProperty =
        AvaloniaProperty.Register<LanguageSelector, IEnumerable>(
            nameof(Languages),
            Array.Empty<object>());

    public static readonly StyledProperty<object?> SelectedLanguageProperty =
        AvaloniaProperty.Register<LanguageSelector, object?>(nameof(SelectedLanguage));

    public IEnumerable Languages
    {
        get => GetValue(LanguagesProperty);
        set => SetValue(LanguagesProperty, value);
    }

    public object? SelectedLanguage
    {
        get => GetValue(SelectedLanguageProperty);
        set => SetValue(SelectedLanguageProperty, value);
    }

    private void DropDownButton_OnClick(object? sender, RoutedEventArgs e) =>
        LanguageAutoCompleteBox.ToggleDropDown();

    public LanguageSelector() => InitializeComponent();
}
