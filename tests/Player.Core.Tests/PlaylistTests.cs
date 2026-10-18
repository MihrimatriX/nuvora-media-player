using Player.App;

namespace Player.Core.Tests;

public class PlaylistTests
{
    sealed class RecordingPlayer : IPlayerService
    {
        public readonly List<string> Commands = [];
        public event Action<string, object?>? PropertyChanged;
        public event Action<string>? PlaybackError { add { } remove { } }
        public void Raise(string name, object? value) => PropertyChanged?.Invoke(name, value);
        public void Initialize(nint windowHandle, IReadOnlyDictionary<string, string> options) { }
        public void Observe(string name, PropertyKind kind) { }
        public void Command(params string[] args) => Commands.Add(string.Join(' ', args));
        public void Dispose() { }
    }

    static (PlayerViewModel Vm, RecordingPlayer Player) Create()
    {
        var player = new RecordingPlayer();
        var vm = new PlayerViewModel(player, new AppSettings());
        player.Raise("playlist", """
            [{"filename":"C:\\Videos\\a.mkv","current":true},{"filename":"C:\\Videos\\b.mkv"},
             {"filename":"https://example.com/live.m3u8","title":"Live"}]
            """);
        player.Commands.Clear();
        return (vm, player);
    }

    [Fact]
    public void Rows_ExposeNumberLabelAndCurrent()
    {
        var (vm, _) = Create();
        Assert.Equal(["1", "2", "3"], vm.PlaylistRows.Select(r => r.Number));
        Assert.Equal("Live", vm.PlaylistRows[2].Label);
        Assert.Equal("example.com", vm.PlaylistRows[2].Location);
        Assert.True(vm.PlaylistRows[0].IsCurrent);
        Assert.Equal("1 / 3", vm.PlaylistPosition);
    }

    [Fact]
    public void MoveDown_TargetsOnePastTheNewPosition()
    {
        var (vm, player) = Create();
        vm.MoveRow(vm.PlaylistRows[0], +1);
        vm.MoveRow(vm.PlaylistRows[2], -1);
        Assert.Equal(["playlist-move 0 2", "playlist-move 2 1"], player.Commands);
    }

    [Fact]
    public void Move_PastEitherEnd_DoesNothing()
    {
        var (vm, player) = Create();
        vm.MoveRow(vm.PlaylistRows[0], -1);
        vm.MoveRow(vm.PlaylistRows[2], +1);
        Assert.Empty(player.Commands);
    }

    [Fact]
    public void Remove_KeepsSelectionAtSameIndexAfterUpdate()
    {
        var (vm, player) = Create();
        vm.RemoveRow(vm.PlaylistRows[1]);
        Assert.Equal(["playlist-remove 1"], player.Commands);
        player.Raise("playlist", """[{"filename":"a.mkv","current":true},{"filename":"c.mkv"}]""");
        Assert.Equal(1, vm.SelectedPlaylistIndex);
    }

    [Fact]
    public void OpeningPlaylist_ClosesOtherSheets()
    {
        var (vm, _) = Create();
        vm.TogglePanelCommand.Execute(null);
        vm.TogglePlaylist();
        Assert.True(vm.IsPlaylistOpen);
        Assert.False(vm.IsPanelOpen);
        Assert.True(vm.IsSidePanelOpen);
        Assert.Equal(0, vm.SelectedPlaylistIndex);
    }
}
