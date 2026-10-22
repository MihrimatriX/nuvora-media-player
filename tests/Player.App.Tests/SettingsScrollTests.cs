using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Player.App.Tests;

public class SettingsScrollTests
{
    [AvaloniaFact]
    public void EveryPage_ScrolledToEnd_LastRowIsNotClipped()
    {
        var vm = new PlayerViewModel(new FakePlayer(), new AppSettings());
        var overlay = new OverlayWindow(vm) { Width = 960, Height = 540 };
        overlay.Show();
        vm.OpenSettingsCommand.Execute(null);
        overlay.UpdateLayout();
        var view = overlay.FindControl<SettingsView>("Settings")!;
        var settings = (SettingsViewModel)view.FindControl<Panel>("Root")!.DataContext!;
        var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();
        var presenter = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First();

        foreach (var page in new[] { "genel", "video", "ses", "altyazi", "disaaktar", "kisayollar", "hakkinda" })
        {
            settings.Page = page;
            overlay.UpdateLayout();
            scroll.ScrollToEnd();
            overlay.UpdateLayout();

            var content = ((Panel)scroll.Content!).Children.Single(c => c.IsVisible);
            var bottom = content.TranslatePoint(new Point(0, content.Bounds.Height), presenter)!.Value.Y;
            // The presenter clips to its padded box, so that box is what the user can actually see.
            var visible = presenter.Bounds.Height - presenter.Padding.Bottom;
            Assert.True(bottom <= visible + 0.5, $"{page}: last row ends at {bottom:0}, visible area ends at {visible:0}");
        }
        overlay.Close();
    }
}
