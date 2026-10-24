namespace Player.Enhancement;

/// <summary>A saved "Görüntü" state: a built-in preset plus the picture sliders, the motion target and the colour /
/// cinema filters. The filter fields have defaults so presets saved before they existed still load.</summary>
public sealed record UserPreset(string Name, string BasePreset, double Contrast, double Saturation, double Brightness,
    double Sharpness, double Gamma, double Hue, int TargetFps,
    string Look = "none", double LookStrength = 100, double Grain = 0, double Vignette = 0, bool Letterbox = false);

/// <summary>Names are unique, compared case-insensitively.</summary>
public static class UserPresets
{
    public const int MaxNameLength = 40;

    public static string CleanName(string? name)
    {
        var n = (name ?? "").Trim();
        return n.Length > MaxNameLength ? n[..MaxNameLength] : n;
    }

    /// <summary>Adds the preset, or overwrites the one with the same name.</summary>
    public static void Save(List<UserPreset> list, UserPreset preset)
    {
        var i = list.FindIndex(p => Same(p.Name, preset.Name));
        if (i >= 0) list[i] = preset; else list.Add(preset);
    }

    /// <summary>False when <paramref name="from"/> is missing, or <paramref name="to"/> is empty or taken by another preset.</summary>
    public static bool Rename(List<UserPreset> list, string from, string to)
    {
        var i = list.FindIndex(p => Same(p.Name, from));
        if (i < 0 || to.Length == 0 || list.Exists(p => Same(p.Name, to) && !Same(p.Name, from))) return false;
        list[i] = list[i] with { Name = to };
        return true;
    }

    public static bool Delete(List<UserPreset> list, string name) => list.RemoveAll(p => Same(p.Name, name)) > 0;

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase);
}
