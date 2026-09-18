using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using EasyChat.Presentation.Shared.Controls;

namespace EasyChat.Presentation.Tests;

[TestClass]
public sealed class LanguageSelectionConverterTests
{
    [TestMethod]
    public void ConvertBack_PreservesNonNullSelection()
    {
        var selection = new object();
        var result = LanguageSelectionConverters.IgnoreNull.ConvertBack(
            selection,
            typeof(object),
            parameter: null,
            CultureInfo.InvariantCulture);

        Assert.AreSame(selection, result);
    }

    [TestMethod]
    public void ConvertBack_IgnoresTransientNullSelection()
    {
        var result = LanguageSelectionConverters.IgnoreNull.ConvertBack(
            value: null,
            typeof(object),
            parameter: null,
            CultureInfo.InvariantCulture);

        Assert.AreSame(BindingOperations.DoNothing, result);
    }

    [TestMethod]
    public void TwoWayBinding_CommitsSelectionButIgnoresTransientNull()
    {
        var initialSelection = new object();
        var updatedSelection = new object();
        var source = new SelectionSource { Selection = initialSelection };
        var target = new AutoCompleteBox();
        target.Bind(
            AutoCompleteBox.SelectedItemProperty,
            new Binding(nameof(SelectionSource.Selection))
            {
                Source = source,
                Mode = BindingMode.TwoWay,
                Converter = LanguageSelectionConverters.IgnoreNull
            });

        Assert.AreSame(initialSelection, target.SelectedItem);

        target.SetCurrentValue(AutoCompleteBox.SelectedItemProperty, updatedSelection);
        Assert.AreSame(updatedSelection, source.Selection);

        target.SetCurrentValue(AutoCompleteBox.SelectedItemProperty, null);
        Assert.AreSame(updatedSelection, source.Selection);
    }

    private sealed class SelectionSource : AvaloniaObject
    {
        public static readonly StyledProperty<object?> SelectionProperty =
            AvaloniaProperty.Register<SelectionSource, object?>(nameof(Selection));

        public object? Selection
        {
            get => GetValue(SelectionProperty);
            set => SetValue(SelectionProperty, value);
        }
    }
}
