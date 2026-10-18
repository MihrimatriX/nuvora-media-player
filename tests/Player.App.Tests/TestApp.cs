using Avalonia;
using Avalonia.Headless;
using Player.App;
using Player.Core;

[assembly: AvaloniaTestApplication(typeof(Player.App.Tests.TestApp))]

namespace Player.App.Tests;

/// <summary>The real <see cref="App"/> (styles, tokens, accent brushes) on the headless platform.
/// No desktop lifetime, so App does not build the main window or touch libmpv.</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseNuvoraFonts();
}

/// <summary>Records commands instead of talking to libmpv.</summary>
sealed class FakePlayer : IPlayerService
{
    public List<string[]> Commands { get; } = [];
#pragma warning disable CS0067 // unused events: the view model only subscribes
    public event Action<string, object?>? PropertyChanged;
    public event Action<string>? PlaybackError;
    public event Action? SeekCompleted;
    public event Action<string>? Log;
#pragma warning restore CS0067
    public void Initialize(nint windowHandle, IReadOnlyDictionary<string, string> options) { }
    public void Observe(string name, PropertyKind kind) { }
    public void Command(params string[] args) => Commands.Add(args);
    public void Dispose() { }
}
