using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using EasyChat.ViewModels;

namespace EasyChat;

/// <summary>
///     Given a view model, returns the corresponding view if possible.
/// </summary>
public class ViewLocator : IDataTemplate
{
    private readonly Dictionary<object, Control> _controlCache = new();

    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var fullName = param.GetType().FullName;

        if (string.IsNullOrWhiteSpace(fullName))
            return new TextBlock { Text = "Type has no name." };

        var name = fullName.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type == null) return new TextBlock { Text = "Not Found: " + name };

        if (!_controlCache.TryGetValue(param, out var res))
        {
            res = (Control)Activator.CreateInstance(type)!;
            _controlCache[param] = res;
        }

        res.DataContext = param;
        return res;
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}