using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Player.App.Tests;

public class AccentTests
{
    [AvaloniaFact]
    public void AccentChange_RetintsSeekBarLive()
    {
        var overlay = new OverlayWindow(new PlayerViewModel(new FakePlayer(), new AppSettings())) { Width = 1280, Height = 720 };
        overlay.Show();
        var seek = overlay.FindControl<Slider>("SeekBar")!;
        seek.ApplyTemplate(); // the control bar is hidden while idle, so nothing templated it yet
        seek.Measure(new Avalonia.Size(800, 24));
        var fill = seek.GetVisualDescendants().OfType<RepeatButton>().First(b => b.Name == "PART_DecreaseButton");

        var hex = SettingsViewModel.Accents[2];
        App.SetAccent(hex);

        Assert.Equal(Color.Parse(hex), ((ISolidColorBrush)seek.Foreground!).Color);
        Assert.Equal(Color.Parse(hex), ((ISolidColorBrush)fill.Background!).Color);
        App.SetAccent(SettingsViewModel.Accents[0]);
        overlay.Close();
    }
}
