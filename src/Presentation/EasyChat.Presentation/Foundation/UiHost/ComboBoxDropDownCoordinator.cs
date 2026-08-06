using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace EasyChat.Presentation.Foundation.UiHost;

/// <summary>
/// Global overlay hygiene for ComboBox dropdowns and Button/attached Flyouts
/// (color pickers, menus): one coherent light-dismiss path.
/// Suki/Fluent light-dismiss is unreliable across overlays and nested scrollers.
/// </summary>
public static class ComboBoxDropDownCoordinator
{
    private static int _installed;
    private static readonly ConditionalWeakTable<TopLevel, object> Attached = new();

    public static void EnsureInstalled()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1)
            return;

        ComboBox.IsDropDownOpenProperty.Changed.AddClassHandler<ComboBox>(OnDropDownOpenChanged);
        FlyoutBase.IsOpenProperty.Changed.AddClassHandler<FlyoutBase>(OnFlyoutOpenChanged);
        // Attach to every window as it loads (main, dialogs, floats).
        Control.LoadedEvent.AddClassHandler<Window>((window, _) => Attach(window));
    }

    public static void Attach(TopLevel topLevel)
    {
        EnsureInstalled();
        if (!Attached.TryAdd(topLevel, null!))
            return;

        topLevel.AddHandler(
            InputElement.PointerPressedEvent,
            OnTopLevelPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private static void OnDropDownOpenChanged(ComboBox combo, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>() != true)
            return;

        var topLevel = TopLevel.GetTopLevel(combo);
        if (topLevel is null)
            return;

        // One combo open; close sibling combos and any color/menu flyouts.
        foreach (var other in topLevel.GetVisualDescendants().OfType<ComboBox>())
        {
            if (!ReferenceEquals(other, combo) && other.IsDropDownOpen)
                other.IsDropDownOpen = false;
        }

        CloseOpenFlyouts(topLevel, exceptHost: null, exceptFlyout: null);
    }

    private static void OnFlyoutOpenChanged(FlyoutBase flyout, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>() != true)
            return;

        var target = flyout.Target;
        var topLevel = target is null ? null : TopLevel.GetTopLevel(target);
        if (topLevel is null)
            return;

        // One flyout open; close other flyouts and any combo dropdowns.
        CloseOpenFlyouts(topLevel, exceptHost: target as Control, exceptFlyout: flyout);
        foreach (var combo in topLevel.GetVisualDescendants().OfType<ComboBox>())
        {
            if (combo.IsDropDownOpen)
                combo.IsDropDownOpen = false;
        }
    }

    private static void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TopLevel topLevel)
            return;

        var source = e.Source as Visual;

        foreach (var combo in topLevel.GetVisualDescendants().OfType<ComboBox>())
        {
            if (!combo.IsDropDownOpen)
                continue;
            if (source is not null && IsPointerInsideHostOrOverlay(source, combo))
                continue;
            combo.IsDropDownOpen = false;
        }

        foreach (var host in EnumerateFlyoutHosts(topLevel))
        {
            var flyout = GetOpenFlyout(host);
            if (flyout is null)
                continue;
            if (source is not null && IsPointerInsideHostOrOverlay(source, host))
                continue;
            // Click is on the host button itself — let the button toggle handle open/close.
            if (source is not null && IsVisualDescendantOf(source, host))
                continue;
            flyout.Hide();
        }
    }

    private static void CloseOpenFlyouts(TopLevel topLevel, Control? exceptHost, FlyoutBase? exceptFlyout)
    {
        foreach (var host in EnumerateFlyoutHosts(topLevel))
        {
            if (exceptHost is not null && ReferenceEquals(host, exceptHost))
                continue;

            var flyout = GetOpenFlyout(host);
            if (flyout is null)
                continue;
            if (exceptFlyout is not null && ReferenceEquals(flyout, exceptFlyout))
                continue;

            flyout.Hide();
        }
    }

    private static IEnumerable<Control> EnumerateFlyoutHosts(TopLevel topLevel)
    {
        foreach (var visual in topLevel.GetVisualDescendants())
        {
            if (visual is not Control control)
                continue;

            if (control is Button { Flyout: not null })
                yield return control;
            else if (FlyoutBase.GetAttachedFlyout(control) is not null)
                yield return control;
        }
    }

    private static FlyoutBase? GetOpenFlyout(Control host)
    {
        if (host is Button { Flyout: { IsOpen: true } buttonFlyout })
            return buttonFlyout;

        var attached = FlyoutBase.GetAttachedFlyout(host);
        return attached is { IsOpen: true } ? attached : null;
    }

    private static bool IsPointerInsideHostOrOverlay(Visual source, Control host)
    {
        if (IsVisualDescendantOf(source, host))
            return true;

        // Popup / ColorView content is re-parented under overlay hosts.
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            var name = visual.GetType().Name;
            if (name.Contains("Popup", StringComparison.Ordinal)
                || name.Contains("Overlay", StringComparison.Ordinal)
                || name.Contains("Flyout", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsVisualDescendantOf(Visual source, Visual ancestor)
    {
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, ancestor))
                return true;
        }

        return false;
    }
}
