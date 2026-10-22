using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Player.App;

/// <summary>One row of the playlist sheet; Index is the mpv playlist index.</summary>
public sealed record PlaylistRow(int Index, PlaylistEntry Entry)
{
    public string Number => (Index + 1).ToString(CultureInfo.InvariantCulture);
    public string Label => Entry.Label;
    public bool IsCurrent => Entry.Current;

    /// <summary>Parent folder for files, host for streams.</summary>
    public string Location => Uri.TryCreate(Entry.Filename, UriKind.Absolute, out var u) && !u.IsFile
        ? u.Host
        : Path.GetFileName(Path.GetDirectoryName(Entry.Filename)) ?? "";
}

/// <summary>Playlist sheet: list, reorder, remove, shuffle. mpv owns the list; every change comes back as a "playlist" update.</summary>
public sealed partial class PlayerViewModel
{
    int _selectAfterUpdate = -1;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsSheetOpen), nameof(ShowTitleBlock), nameof(ShowFileChip), nameof(IsSidePanelOpen))]
    public partial bool IsPlaylistOpen { get; set; }

    [ObservableProperty] public partial int SelectedPlaylistIndex { get; set; } = -1;

    /// <summary>A right-hand sheet covers the bar, so the thin progress line shows instead.</summary>
    public bool IsSidePanelOpen => IsPanelOpen || IsPlaylistOpen;

    public IReadOnlyList<PlaylistRow> PlaylistRows => Playlist.Select((e, i) => new PlaylistRow(i, e)).ToList();
    public bool HasPlaylist => Playlist.Count > 0;
    public string PlaylistPosition
    {
        get
        {
            var current = Playlist.ToList().FindIndex(e => e.Current);
            return Playlist.Count == 0 ? "" : current < 0 ? $"{Playlist.Count}" : $"{current + 1} / {Playlist.Count}";
        }
    }

    partial void OnPlaylistChanged(IReadOnlyList<PlaylistEntry> value)
    {
        OnPropertyChanged(nameof(PlaylistRows));
        OnPropertyChanged(nameof(HasPlaylist));
        OnPropertyChanged(nameof(PlaylistPosition));
        if (_selectAfterUpdate >= 0)
        {
            SelectedPlaylistIndex = Math.Min(_selectAfterUpdate, value.Count - 1);
            _selectAfterUpdate = -1;
        }
    }

    [RelayCommand]
    public void TogglePlaylist()
    {
        IsPlaylistOpen = !IsPlaylistOpen;
        IsPanelOpen = false;
        IsSettingsOpen = false;
        IsSubtitleSearchOpen = false;
        if (IsPlaylistOpen) SelectedPlaylistIndex = Playlist.ToList().FindIndex(e => e.Current);
    }

    [RelayCommand]
    public void PlayRow(PlaylistRow? row)
    {
        if (row is null || row.IsCurrent) return;
        SaveResumePoint();
        PlayIndex(row.Index);
    }

    [RelayCommand]
    public void RemoveRow(PlaylistRow? row)
    {
        if (row is null) return;
        _selectAfterUpdate = row.Index;
        _player.Command("playlist-remove", I(row.Index));
    }

    [RelayCommand] public void MoveRowUp(PlaylistRow? row) => MoveRow(row, -1);
    [RelayCommand] public void MoveRowDown(PlaylistRow? row) => MoveRow(row, +1);

    public void MoveRow(PlaylistRow? row, int delta)
    {
        if (row is null) return;
        var target = row.Index + delta;
        if (target < 0 || target >= Playlist.Count) return;
        _selectAfterUpdate = target;
        // playlist-move puts the entry *before* index2, so moving down needs one past the target.
        _player.Command("playlist-move", I(row.Index), I(delta > 0 ? target + 1 : target));
    }

    [RelayCommand]
    public void ShufflePlaylist()
    {
        _player.Command("playlist-shuffle");
        ShowOsd(Loc.T("playlist.shuffled"));
    }

    /// <summary>Removes everything except the file that is playing.</summary>
    [RelayCommand]
    public void ClearPlaylist() => _player.Command("playlist-clear");

    public void AppendToPlaylist(IReadOnlyList<string> paths)
    {
        foreach (var p in paths) _player.Command("loadfile", p, IsIdle ? "append-play" : "append");
        if (paths.Count > 0) ShowOsd(paths.Count == 1 ? Loc.T("playlist.addedOne") : Loc.F("playlist.addedMany", paths.Count));
    }

    static string I(int i) => i.ToString(CultureInfo.InvariantCulture);
}
