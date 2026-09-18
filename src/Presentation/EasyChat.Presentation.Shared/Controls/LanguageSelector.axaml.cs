using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

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

    public static readonly StyledProperty<LanguageSortMode> SortModeProperty =
        AvaloniaProperty.Register<LanguageSelector, LanguageSortMode>(
            nameof(SortMode),
            LanguageSortMode.Popularity);

    private readonly ObservableCollection<object?> _sortedLanguages = [];
    private INotifyCollectionChanged? _observedLanguages;

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

    public LanguageSortMode SortMode
    {
        get => GetValue(SortModeProperty);
        set => SetValue(SortModeProperty, value);
    }

    public IEnumerable SortedLanguages => _sortedLanguages;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LanguagesProperty || change.Property == SortModeProperty)
            RefreshSortedLanguages();
    }

    public LanguageSelector()
    {
        InitializeComponent();
        RefreshSortedLanguages();
    }

    private void RefreshSortedLanguages()
    {
        if (_observedLanguages is not null)
            _observedLanguages.CollectionChanged -= OnLanguagesCollectionChanged;

        _observedLanguages = Languages as INotifyCollectionChanged;
        if (_observedLanguages is not null)
            _observedLanguages.CollectionChanged += OnLanguagesCollectionChanged;

        _sortedLanguages.Clear();
        foreach (var item in LanguageSelectorSorting.Sort(Languages, SortMode))
            _sortedLanguages.Add(item);
    }

    private void OnLanguagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RefreshSortedLanguages();

    private void DropDownButton_OnClick(object? sender, RoutedEventArgs e) =>
        LanguageAutoCompleteBox.ToggleDropDown();
}
