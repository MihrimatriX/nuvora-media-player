using Player.Core;

namespace Player.Core.Tests;

public class SingleInstanceTests
{
    static string NewId() => $"nuvora-test-{Guid.NewGuid():n}";

    [Fact]
    public void SecondAcquire_Fails_UntilOwnerDisposes()
    {
        var id = NewId();
        var owner = SingleInstance.TryAcquire(id);
        Assert.NotNull(owner);
        Assert.Null(SingleInstance.TryAcquire(id));

        owner.Dispose();
        using var next = SingleInstance.TryAcquire(id);
        Assert.NotNull(next);
    }

    [Fact]
    public async Task TrySend_DeliversArgumentsToOwner()
    {
        var id = NewId();
        using var owner = SingleInstance.TryAcquire(id)!;
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.ArgumentsReceived += args => received.TrySetResult(args);
        owner.StartListening();

        string[] sent = [@"C:\Videos\film ğüş.mkv", "https://example.com/stream.m3u8"];
        Assert.True(await Task.Run(() => SingleInstance.TrySend(id, sent, TimeSpan.FromSeconds(5))));

        Assert.Equal(sent, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task TrySend_KeepsServing_AfterFirstClient()
    {
        var id = NewId();
        using var owner = SingleInstance.TryAcquire(id)!;
        var count = 0;
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.ArgumentsReceived += _ => { if (Interlocked.Increment(ref count) == 2) second.TrySetResult(); };
        owner.StartListening();

        Assert.True(await Task.Run(() => SingleInstance.TrySend(id, ["a"], TimeSpan.FromSeconds(5))));
        Assert.True(await Task.Run(() => SingleInstance.TrySend(id, ["b"], TimeSpan.FromSeconds(5))));
        await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void TrySend_NoListener_ReturnsFalse()
    {
        Assert.False(SingleInstance.TrySend(NewId(), ["x"], TimeSpan.FromMilliseconds(200)));
    }
}
