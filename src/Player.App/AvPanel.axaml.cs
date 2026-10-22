using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Player.App;

public partial class AvPanel : UserControl
{
    public AvPanel()
    {
        InitializeComponent();
        Accessibility.LabelRows(this);
    }

    PlayerViewModel Vm => (PlayerViewModel)DataContext!;

    /// <summary>Built-in profiles, the user's presets, then save / rename / delete (the last two act on the picked user preset).</summary>
    void OnProfiles(object? sender, RoutedEventArgs e)
    {
        var anchor = (Control)sender!;
        List<object> items = [.. PlayerViewModel.Profiles.Select(p => Menus.Item(Loc.T($"profile.{p}"), p == Vm.ProfileName, () => Vm.ApplyProfile(p)))];
        if (Vm.UserPresetList.Count > 0) items.Add(new Separator());
        items.AddRange(Vm.UserPresetList.Select(u => Menus.Item(u.Name, u.Name == Vm.CurrentUserPreset, () => Vm.ApplyUserPreset(u))));
        items.Add(new Separator());
        items.Add(Menus.Action(Loc.T("profile.save"), () => Menus.Prompt(anchor, Loc.T("profile.name"), Vm.CurrentUserPreset ?? "", Vm.SaveUserPreset)));
        if (Vm.CurrentUserPreset is { } current)
        {
            items.Add(Menus.Action(Loc.F("profile.rename", current), () => Menus.Prompt(anchor, Loc.T("profile.name"), current, n => Vm.RenameUserPreset(current, n))));
            items.Add(Menus.Action(Loc.F("profile.delete", current), () => Vm.DeleteUserPreset(current)));
        }
        Menus.Show(anchor, items);
    }

    void OnDrc(object? sender, RoutedEventArgs e) =>
        Menus.Show((Control)sender!, PlayerViewModel.DrcLevels.Select((d, i) => Menus.Item(Loc.T(d.Label), i == Vm.DrcIndex, () => Vm.DrcIndex = i)));

    void OnChannelWheel(object? sender, PointerWheelEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ChannelGain channel) return;
        channel.Gain = Math.Clamp(channel.Gain + Math.Sign(e.Delta.Y) * 10, 0, 200);
        Vm.SelectChannelCommand.Execute(channel);
        e.Handled = true; // don't also change the master volume
    }
}
