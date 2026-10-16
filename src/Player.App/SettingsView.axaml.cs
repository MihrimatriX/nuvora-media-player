using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Player.Enhancement;

namespace Player.App;

public partial class SettingsView : UserControl
{
    SettingsViewModel? _vm;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null || DataContext is not PlayerViewModel player) return;
            Root.DataContext = _vm = new SettingsViewModel(player);
            _vm.Reload();
            BuildSwatches();
        };
    }

    SettingsViewModel Vm => _vm!;

    void BuildSwatches()
    {
        if (_vm is null) return;
        Swatches.Children.Clear();
        for (var i = 0; i < SettingsViewModel.Accents.Length; i++)
        {
            var hex = SettingsViewModel.Accents[i];
            var swatch = new Button { Background = Brush.Parse(hex) };
            swatch.Classes.Add("swatch");
            swatch.Classes.Set("on", string.Equals(Vm.Draft.Accent, hex, StringComparison.OrdinalIgnoreCase));
            Avalonia.Automation.AutomationProperties.SetName(swatch, SettingsViewModel.AccentNames[i]);
            swatch.Click += (_, _) =>
            {
                Vm.Set(s => s.Accent = hex);
                App.SetAccent(hex); // live preview; "Vazgeç" restores
                BuildSwatches();
            };
            Swatches.Children.Add(swatch);
        }
    }

    public void Reload()
    {
        if (_vm is null) return;
        _vm.Reload();
        BuildSwatches();
    }

    public bool HandleKey(KeyEventArgs e)
    {
        if (_vm?.Capturing is null) return false;
        if (e.Key == Key.Escape) _vm.CancelCapture();
        else if (e.Key is Key.Back or Key.Delete) _vm.Assign("");
        else if (Shortcuts.FromKey(e.Key, e.KeySymbol, e.KeyModifiers) is { } binding) _vm.Assign(binding);
        e.Handled = true;
        return true;
    }

    public void Cancel() => _vm?.CancelCommand.Execute(null);

    void OnBackdrop(object? sender, PointerPressedEventArgs e) => Cancel();

    void OnLanguage(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
    [
        Menus.Item("Türkçe", true, () => { }),
        Menus.Item("English (yakında)", false, () => { }, enabled: false),
    ]);

    void OnTheme(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.Accents.Select((hex, i) => Menus.Item(SettingsViewModel.AccentNames[i],
            string.Equals(hex, Vm.Draft.Accent, StringComparison.OrdinalIgnoreCase),
            () =>
            {
                Vm.Set(d => d.Accent = hex);
                App.SetAccent(hex);
                BuildSwatches();
            })));

    void OnWheelAction(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.WheelActions.Select(w => Menus.Item(w.Label, w.Value == Vm.Draft.WheelAction, () => Vm.Set(d => d.WheelAction = w.Value))));

    void OnLoopMode(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.LoopModes.Select(l => Menus.Item(l.Label, l.Value == Vm.Draft.LoopMode, () => Vm.Set(d => d.LoopMode = l.Value))));

    void OnSeekStep(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.SeekSteps.Select(s => Menus.Item($"{s} sn", s == Vm.Draft.SeekStepSeconds, () => Vm.Set(d => d.SeekStepSeconds = s))));

    void OnVolumeStep(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.VolumeSteps.Select(s => Menus.Item($"%{s}", s == Vm.Draft.VolumeStep, () => Vm.Set(d => d.VolumeStep = s))));

    void OnSubtitleScale(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.SubtitleScales.Select(s => Menus.Item($"%{(s * 100).ToString("0", CultureInfo.InvariantCulture)}",
            Math.Abs(s - Vm.Draft.SubtitleScale) < 0.01, () => Vm.Set(d => d.SubtitleScale = s))));

    void OnSubtitlePos(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.SubtitlePositions.Select(p => Menus.Item(p.Label, p.Value == Vm.Draft.SubtitlePosition, () => Vm.Set(d => d.SubtitlePosition = p.Value))));

    void OnSubtitleColor(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.SubtitleColors.Select(c => Menus.Item(c.Label, c.Value == Vm.Draft.SubtitleColor, () => Vm.Set(d => d.SubtitleColor = c.Value))));

    void OnDefaultPreset(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        ImagePresets.All.Select(p => Menus.Item(p.Name, p.Id == Vm.Draft.DefaultPreset, () => Vm.Set(d => d.DefaultPreset = p.Id))));

    void OnHwdecMode(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.HwdecModes.Select(h => Menus.Item(h.Label, h.Value == Vm.Draft.HwdecMode, () => Vm.Set(d => d.HwdecMode = h.Value))));

    void OnDeinterlaceMode(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.DeinterlaceModes.Select(m => Menus.Item(m.Label, m.Value == Vm.Draft.DeinterlaceMode, () => Vm.Set(d =>
        {
            d.DeinterlaceMode = m.Value;
            d.Deinterlace = m.Value != "no";
        }))));

    void OnScreenFormat(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.ScreenFormats.Select(s => Menus.Item(s.Label, s.Value == Vm.Draft.ScreenFormat, () => Vm.Set(d => d.ScreenFormat = s.Value))));

    void OnDisplayFormat43(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.DisplayFormats.Select(f => Menus.Item(f.Label, f.Value == Vm.Draft.DisplayFormat43, () => Vm.Set(d => d.DisplayFormat43 = f.Value))));

    void OnDisplayFormat169(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.DisplayFormats.Select(f => Menus.Item(f.Label, f.Value == Vm.Draft.DisplayFormat169, () => Vm.Set(d => d.DisplayFormat169 = f.Value))));

    void OnDisplayFormatCinematic(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.CinematicFormats.Select(f => Menus.Item(f.Label, f.Value == Vm.Draft.DisplayFormatCinematic, () => Vm.Set(d => d.DisplayFormatCinematic = f.Value))));

    void OnVideoOutput(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.VideoOutputs.Select(o => Menus.Item(o.Label, o.Value == Vm.Draft.VideoOutput, () => Vm.Set(d => d.VideoOutput = o.Value))));

    void OnTargetFps(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.FpsOptions.Select(f => Menus.Item(f.Label, f.Value == Vm.Draft.TargetFps, () => Vm.Set(d =>
        {
            d.TargetFps = f.Value;
            d.SmoothMotionByDefault = f.Value != 0;
        }))));

    void OnMotionScaler(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.MotionScalers.Select(m => Menus.Item(m.Label, m.Value == Vm.Draft.MotionScaler, () => Vm.Set(d => d.MotionScaler = m.Value))));

    void OnToneMapping(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.ToneMappings.Select(t => Menus.Item(t.Label, t.Value == Vm.Draft.ToneMapping, () => Vm.Set(d => d.ToneMapping = t.Value))));

    void OnScreenshotFormat(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.ScreenshotFormats.Select(f => Menus.Item(f.ToUpperInvariant(), f == Vm.Draft.ScreenshotFormat, () => Vm.Set(d => d.ScreenshotFormat = f))));

    void OnAudioOutputMode(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.AudioOutputModes.Select(a => Menus.Item(a.Label, a.Value == Vm.Draft.AudioOutputMode, () => Vm.Set(d => d.AudioOutputMode = a.Value))));

    void OnAudioDevice(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        Vm.Player.AudioDevices.Select(d => Menus.Item(d.Description, d.Name == Vm.Draft.AudioDevice, () => Vm.Set(s => s.AudioDevice = d.Name))));

    void OnSpdifDevice(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        Vm.Player.AudioDevices.Select(d => Menus.Item(d.Description, d.Name == Vm.Draft.SpdifDevice, () => Vm.Set(s => s.SpdifDevice = d.Name))));

    void OnHdmiDevice(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        Vm.Player.AudioDevices.Select(d => Menus.Item(d.Description, d.Name == Vm.Draft.HdmiDevice, () => Vm.Set(s => s.HdmiDevice = d.Name))));

    void OnExportEncoder(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.ExportEncoders.Select(enc => Menus.Item(enc.Label, enc.Value == Vm.Draft.ExportEncoder, () => Vm.Set(d => d.ExportEncoder = enc.Value))));

    void OnExportDeinterlace(object? sender, RoutedEventArgs e) => Menus.Show((Control)sender!,
        SettingsViewModel.ExportDeinterlaceModes.Select(m => Menus.Item(m.Label, m.Value == Vm.Draft.ExportDeinterlace, () => Vm.Set(d => d.ExportDeinterlace = m.Value))));

    async void OnPickFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new() { Title = "Kare yakalama klasörü" });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) Vm.Set(d => d.ScreenshotDirectory = path);
    }

    async void OnPickExportFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFolderPickerAsync(new() { Title = "Dışa aktarma klasörü" });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) Vm.Set(d => d.ExportDirectory = path);
    }
}
