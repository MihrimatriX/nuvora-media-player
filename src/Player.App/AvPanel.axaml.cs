using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Player.App;

public partial class AvPanel : UserControl
{
    public AvPanel() => InitializeComponent();

    PlayerViewModel Vm => (PlayerViewModel)DataContext!;

    void OnProfiles(object? sender, RoutedEventArgs e) =>
        Menus.Show((Control)sender!, PlayerViewModel.Profiles.Select(p => Menus.Item(p, p == Vm.ProfileName, () => Vm.ApplyProfile(p))));

    void OnDrc(object? sender, RoutedEventArgs e) =>
        Menus.Show((Control)sender!, PlayerViewModel.DrcLevels.Select((d, i) => Menus.Item(d.Label, i == Vm.DrcIndex, () => Vm.DrcIndex = i)));

    void OnChannelWheel(object? sender, PointerWheelEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ChannelGain channel) return;
        channel.Gain = Math.Clamp(channel.Gain + Math.Sign(e.Delta.Y) * 10, 0, 200);
        Vm.SelectChannelCommand.Execute(channel);
        e.Handled = true; // don't also change the master volume
    }
}
