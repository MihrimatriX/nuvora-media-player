using System.IO.Pipes;
using System.Text.Json;

namespace Player.Core;

/// <summary>One running player per user session. The first process owns a named mutex and listens on a
/// named pipe; later launches send their command line there (e.g. files opened from Explorer) and exit.</summary>
public sealed class SingleInstance : IDisposable
{
    readonly Mutex _mutex;
    readonly string _pipeName;
    readonly CancellationTokenSource _cts = new();
    Task? _listener;

    /// <summary>Raised on a background thread with the forwarded arguments.</summary>
    public event Action<string[]>? ArgumentsReceived;

    SingleInstance(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    /// <summary>Returns the owner handle, or null when another process already holds <paramref name="id"/>.</summary>
    public static SingleInstance? TryAcquire(string id)
    {
        // Not owned, only held open: ownership is thread-affine, and the handle alone marks us as running.
        var mutex = new Mutex(initiallyOwned: false, MutexName(id), out var createdNew);
        if (createdNew) return new SingleInstance(mutex, PipeName(id));
        mutex.Dispose();
        return null;
    }

    /// <summary>Hands <paramref name="args"/> to the running instance. False when it did not answer in time,
    /// in which case the caller should start normally rather than lose the files.</summary>
    public static bool TrySend(string id, string[] args, TimeSpan timeout)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName(id), PipeDirection.Out);
            pipe.Connect(timeout);
            JsonSerializer.Serialize(pipe, args);
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void StartListening() => _listener ??= Task.Run(ListenAsync);

    async Task ListenAsync()
    {
        var server = CreateServer();
        try
        {
            while (true)
            {
                await server.WaitForConnectionAsync(_cts.Token);
                // The next instance listens before this one is read, so back-to-back launches never find no pipe.
                var connected = server;
                server = CreateServer();
                _ = ReadAsync(connected);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            await server.DisposeAsync();
        }
    }

    NamedPipeServerStream CreateServer() => new(_pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    async Task ReadAsync(NamedPipeServerStream pipe)
    {
        await using (pipe)
        {
            try
            {
                var args = await JsonSerializer.DeserializeAsync<string[]>(pipe, cancellationToken: _cts.Token);
                if (args is not null) ArgumentsReceived?.Invoke(args);
            }
            catch (Exception e) when (e is IOException or JsonException or OperationCanceledException)
            {
                // A client that died mid-write, or shutdown; nothing to open.
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener?.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
        _mutex.Dispose();
        _cts.Dispose();
    }

    // Scoped to the user so two accounts on one machine each get their own player.
    static string MutexName(string id) => $"{id}-{Environment.UserName}";
    static string PipeName(string id) => $"{id}-{Environment.UserName}-args";
}
