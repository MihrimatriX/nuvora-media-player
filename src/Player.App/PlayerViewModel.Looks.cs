using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Player.Enhancement;

namespace Player.App;

public sealed record LookChoice(ColorLook Look, bool IsSelected)
{
    public string Id => Look.Id;
    public string Name => Loc.T($"look.{Look.Id}");
    public string Description => Loc.T($"look.{Look.Id}.desc");
    public IBrush Preview => LookPreviews.For(Look);
}

/// <summary>Swatch for a look's tile: a few reference colours (shadow, skin, foliage, sky, highlight) run through the look.</summary>
static class LookPreviews
{
    static readonly (double R, double G, double B)[] Reference =
        [(0.10, 0.12, 0.18), (0.86, 0.64, 0.52), (0.35, 0.55, 0.30), (0.45, 0.68, 0.92), (0.98, 0.93, 0.82)];

    static readonly Dictionary<string, IBrush> Cache = [];

    public static IBrush For(ColorLook look)
    {
        if (Cache.TryGetValue(look.Id, out var brush)) return brush;
        var stops = new GradientStops();
        for (var i = 0; i < Reference.Length; i++)
        {
            var (r, g, b) = look.Apply(Reference[i].R, Reference[i].G, Reference[i].B);
            stops.Add(new GradientStop(Color.FromRgb(Byte(r), Byte(g), Byte(b)), i / (double)(Reference.Length - 1)));
        }
        brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = stops,
        };
        Cache[look.Id] = brush;
        return brush;
    }

    static byte Byte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
}

/// <summary>"Renk ve Sinema Filtreleri": colour looks plus film grain, vignette and cinemascope bars (looks.glsl).</summary>
public sealed partial class PlayerViewModel
{
    /// <summary>Amounts the context menu switches grain and vignette on with.</summary>
    const double DefaultGrain = 35, DefaultVignette = 45;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(LookChoices), nameof(LookName), nameof(HasLook), nameof(AfterBadgeText), nameof(HasCinemaFilters))]
    public partial string LookId { get; set; } = "none";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(LookStrengthText))]
    public partial double LookStrength { get; set; } = 100;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(GrainText), nameof(HasCinemaFilters))]
    public partial double Grain { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(VignetteText), nameof(HasCinemaFilters))]
    public partial double Vignette { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCinemaFilters))]
    public partial bool Letterbox { get; set; }

    public IEnumerable<LookChoice> LookChoices => ColorLooks.All.Select(l => new LookChoice(l, l.Id == LookId));
    public string LookName => Loc.T($"look.{CurrentLook.Id}");
    public bool HasLook => LookId != ColorLooks.None.Id;
    public bool HasCinemaFilters => HasLook || Grain > 0 || Vignette > 0 || Letterbox;
    public string LookStrengthText => $"{LookStrength:0}%";
    public string GrainText => $"{Grain:0}";
    public string VignetteText => $"{Vignette:0}";

    ColorLook CurrentLook => ColorLooks.Find(LookId);

    static string LooksShaderPath => ShaderPath("looks.glsl");

    /// <summary>Bars only make sense on an upright picture: looks.glsl runs before mpv rotates the frame.</summary>
    double LetterboxBars => Letterbox && Rotation is 0 or 180 ? ColorLooks.LetterboxFraction(DisplayAspect) : 0;

    /// <summary>The filter pass is loaded only while it changes something, and never for the "original" view.</summary>
    bool LooksActive => !IsBypassed && (HasLook || Grain > 0 || Vignette > 0 || LetterboxBars > 0);

    string[] LookShaders() => LooksActive && File.Exists(LooksShaderPath) ? [LooksShaderPath] : [];

    [RelayCommand]
    void SelectLook(string id) => LookId = ColorLooks.Find(id).Id;

    [RelayCommand]
    public void ResetLooks()
    {
        LookId = ColorLooks.None.Id;
        LookStrength = 100;
        (Grain, Vignette, Letterbox) = (0, 0, false);
    }

    /// <summary>Next look, wrapping back to "none"; the shortcut shows its name.</summary>
    public void CycleLook()
    {
        var i = ColorLooks.All.ToList().FindIndex(l => l.Id == LookId);
        LookId = ColorLooks.All[(i + 1) % ColorLooks.All.Count].Id;
        ShowOsd(Loc.F("osd.look", LookName));
    }

    public void ToggleGrain() => Grain = Grain > 0 ? 0 : DefaultGrain;
    public void ToggleVignette() => Vignette = Vignette > 0 ? 0 : DefaultVignette;

    partial void OnLookIdChanged(string value) => ApplyImage();
    partial void OnLookStrengthChanged(double value) => UpdateShaderOpts();
    partial void OnGrainChanged(double value) => ApplyImage();
    partial void OnVignetteChanged(double value) => ApplyImage();
    partial void OnLetterboxChanged(bool value) => ApplyImage();

    /// <summary>One glsl-shader-opts string for every loaded Nuvora shader. "split" is shared: compare.glsl and
    /// looks.glsl both leave the left side of the bar untouched, so the looks belong to the "after" half.</summary>
    void UpdateShaderOpts()
    {
        var comparing = IsSplitCompare && !IsBypassed;
        var looks = LooksActive;
        if (!comparing && !looks) return; // nothing loaded reads the options
        var parts = new List<string> { $"split={Inv(comparing ? _videoSplitRatio : 0)}" };
        if (comparing) parts.Add(CompareShaderOpts());
        if (looks) parts.Add(ColorLooks.ShaderOptions(CurrentLook, LookStrength / 100, Grain / 100, Vignette / 100, LetterboxBars));
        Set("glsl-shader-opts", string.Join(',', parts));
    }
}
