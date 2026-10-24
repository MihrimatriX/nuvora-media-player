using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Player.Enhancement;

namespace Player.App.Tests;

public class AvPanelTests
{
    static (Window Window, AvPanel Panel, PlayerViewModel Vm, FakePlayer Player) Show()
    {
        var player = new FakePlayer();
        var vm = new PlayerViewModel(player, new AppSettings()) { IsPanelOpen = true };
        var panel = new AvPanel { DataContext = vm };
        var window = new Window { Content = panel, Width = 1100, Height = 1600 };
        window.Show();
        return (window, panel, vm, player);
    }

    static List<RadioButton> Tiles(AvPanel panel) =>
        panel.GetVisualDescendants().OfType<RadioButton>().Where(r => r.Classes.Contains("look")).ToList();

    [AvaloniaFact]
    public void LookTiles_OnePerLook_NoneSelected()
    {
        var (_, panel, _, _) = Show();
        var tiles = Tiles(panel);

        Assert.Equal(ColorLooks.All.Count, tiles.Count);
        Assert.Equal("none", Assert.Single(tiles, t => t.IsChecked == true).CommandParameter);
    }

    [AvaloniaFact]
    public void LookTileClick_AppliesTheLook_AndLoadsTheFilterShader()
    {
        var (window, panel, vm, player) = Show();
        var noir = Tiles(panel).Single(t => (string?)t.CommandParameter == "noir");
        var center = noir.TranslatePoint(new Point(noir.Bounds.Width / 2, noir.Bounds.Height / 2), window)!.Value;

        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);

        Assert.Equal("noir", vm.LookId);
        Assert.Contains(player.Commands, c => c[0] == "change-list" && c[^1].EndsWith("looks.glsl"));
        Assert.Equal("noir", Assert.Single(Tiles(panel), t => t.IsChecked == true).CommandParameter);
    }
}
