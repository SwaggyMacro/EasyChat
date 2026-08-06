using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using EasyChat.Presentation.Foundation.Navigation;

namespace EasyChat.Desktop;

public sealed class ViewLocator : IDataTemplate
{
    private readonly Dictionary<object, Control> _views = new();

    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        var viewName = ViewTypeConvention.GetViewTypeName(data.GetType());
        var viewType = viewName is null
            ? null
            : typeof(Presentation.AssemblyMarker).Assembly.GetType(viewName);
        if (viewType is null || !typeof(Control).IsAssignableFrom(viewType))
            return new TextBlock { Text = $"Not Found: {viewName}" };

        if (!_views.TryGetValue(data, out var view))
        {
            view = (Control)Activator.CreateInstance(viewType)!;
            // Pages must fill the side-menu content host or * rows never receive height.
            view.HorizontalAlignment = HorizontalAlignment.Stretch;
            view.VerticalAlignment = VerticalAlignment.Stretch;
            _views.Add(data, view);
        }

        view.DataContext = data;
        return view;
    }

    public bool Match(object? data) => data is ConventionViewModelBase;
}
