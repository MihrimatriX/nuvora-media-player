using Avalonia.Controls;
using Avalonia.Threading;

namespace Player.App;

/// <summary>Drop-down and context menus built on demand (only one flyout can ever be open at a time).</summary>
static class Menus
{
    static MenuFlyout? _current;

    public static bool IsOpen => _current?.IsOpen == true;

    public static void HideCurrent()
    {
        if (_current is not { } old) return;
        _current = null;
        old.Hide();
    }

    public static MenuItem Item(string header, bool selected, Action select, bool enabled = true)
    {
        var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.Radio, IsChecked = selected, IsEnabled = enabled };
        item.Click += (_, _) => select();
        return item;
    }

    public static MenuItem Action(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    public static void Show(Control anchor, IEnumerable<object> items, Action<bool>? openChanged = null, bool atPointer = false)
    {
        HideCurrent();

        var flyout = new MenuFlyout();
        foreach (var i in items) flyout.Items.Add(i);
        flyout.Opened += (_, _) => openChanged?.Invoke(true);
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(_current, flyout)) _current = null;
            openChanged?.Invoke(false);
        };
        _current = flyout;
        flyout.ShowAt(anchor, atPointer);
    }
}
