using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Player.App;

public partial class PlaylistPanel : UserControl
{
    public PlaylistPanel() => InitializeComponent();

    PlayerViewModel Vm => (PlayerViewModel)DataContext!;
    PlaylistRow? Selected => List.SelectedItem as PlaylistRow;

    void OnAdd(object? sender, RoutedEventArgs e) => ((OverlayWindow)TopLevel.GetTopLevel(this)!).AppendFiles();

    void OnListDoubleTapped(object? sender, TappedEventArgs e) => Vm.PlayRow(Selected);

    /// <summary>Keyboard equivalents of the row buttons; arrows without Alt keep their ListBox meaning.</summary>
    void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: Vm.PlayRow(Selected); break;
            case Key.Delete: Vm.RemoveRow(Selected); break;
            case Key.Up when e.KeyModifiers == KeyModifiers.Alt: Vm.MoveRow(Selected, -1); break;
            case Key.Down when e.KeyModifiers == KeyModifiers.Alt: Vm.MoveRow(Selected, +1); break;
            default: return;
        }
        e.Handled = true;
    }
}
