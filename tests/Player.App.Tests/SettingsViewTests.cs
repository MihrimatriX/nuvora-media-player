using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Player.App.Tests;

public class SettingsViewTests
{
    static (Window Window, SettingsView View, SettingsViewModel Vm) Show()
    {
        var view = new SettingsView();
        var window = new Window { Content = view, Width = 1100, Height = 760 };
        window.Show();
        view.DataContext = new PlayerViewModel(new FakePlayer(), new AppSettings());
        var vm = (SettingsViewModel)view.FindControl<Panel>("Root")!.DataContext!;
        return (window, view, vm);
    }

    [AvaloniaFact]
    public void Swatches_OnePerAccent_CurrentOneMarked()
    {
        var (_, view, vm) = Show();
        var swatches = view.FindControl<StackPanel>("Swatches")!.Children.OfType<Button>().ToList();

        Assert.Equal(SettingsViewModel.Accents.Length, swatches.Count);
        var on = Assert.Single(swatches, b => b.Classes.Contains("on"));
        Assert.Equal(Color.Parse(vm.Draft.Accent), ((ISolidColorBrush)on.Background!).Color);
    }

    [AvaloniaFact]
    public void SwatchClick_PreviewsAccent_AndUpdatesDraft()
    {
        var (_, view, vm) = Show();
        var second = view.FindControl<StackPanel>("Swatches")!.Children.OfType<Button>().ElementAt(1);

        second.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var hex = SettingsViewModel.Accents[1];
        Assert.Equal(hex, vm.Draft.Accent, ignoreCase: true);
        Assert.Equal(Color.Parse(hex), ((ISolidColorBrush)Avalonia.Application.Current!.Resources["Accent"]!).Color);
        var swatches = view.FindControl<StackPanel>("Swatches")!.Children.OfType<Button>().ToList();
        Assert.Contains("on", swatches[1].Classes);
    }

    [AvaloniaFact]
    public void ShortcutCapture_AssignsKey_AndTakesItFromThePreviousOwner()
    {
        var (_, view, vm) = Show();
        var pause = vm.ShortcutItems.Single(s => s.Action.Id == "pause");
        var mute = vm.ShortcutItems.Single(s => s.Action.Id == "mute");
        Assert.Equal("M", mute.Binding);

        vm.StartCaptureCommand.Execute(pause);
        Assert.Same(pause, vm.Capturing);
        Assert.True(view.HandleKey(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.M, KeySymbol = "m" }));

        Assert.Equal("M", pause.Binding);
        Assert.Equal("", mute.Binding);
        Assert.Null(vm.Capturing);
    }

    [AvaloniaFact]
    public void ShortcutCapture_Escape_KeepsBinding()
    {
        var (_, view, vm) = Show();
        var target = vm.ShortcutItems[0];
        var before = target.Binding;

        vm.StartCaptureCommand.Execute(target);
        view.HandleKey(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });

        Assert.Equal(before, target.Binding);
        Assert.Null(vm.Capturing);
    }
}
