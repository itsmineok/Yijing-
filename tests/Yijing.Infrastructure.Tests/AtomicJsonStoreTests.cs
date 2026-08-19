using System.Text.Json;
using Yijing.Infrastructure.Storage;

namespace Yijing.Infrastructure.Tests;

public sealed class AtomicJsonStoreTests
{
    [Fact]
    public async Task SaveAsync_ReplacesExistingJsonAndRemovesTempFile()
    {
        using var directory = new TemporaryDirectory();
        var store = new AtomicJsonStore(directory.Path);
        await store.WriteAsync("active-game.json", new { revision = 1 });
        await store.WriteAsync("active-game.json", new { revision = 2 });

        var value = await store.ReadAsync<Dictionary<string, int>>("active-game.json");

        Assert.Equal(2, value!["revision"]);
        Assert.False(File.Exists(Path.Combine(directory.Path, "active-game.json.tmp")));
    }

    [Fact]
    public async Task ReadAsync_ReturnsNullWhenFileDoesNotExist()
    {
        using var directory = new TemporaryDirectory();
        var store = new AtomicJsonStore(directory.Path);

        Assert.Null(await store.ReadAsync<Dictionary<string, int>>("missing.json"));
    }

    [Fact]
    public async Task ReadAsync_PropagatesJsonExceptionForCorruptFile()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "bad.json"), "{bad");
        var store = new AtomicJsonStore(directory.Path);

        await Assert.ThrowsAsync<JsonException>(() => store.ReadAsync<object>("bad.json"));
    }

    [Fact]
    public async Task ConcurrentWrites_ProduceValidJsonAndLeaveNoTemporaryFiles()
    {
        using var directory = new TemporaryDirectory();
        var store = new AtomicJsonStore(directory.Path);
        var writes = Enumerable.Range(1, 32).Select(revision =>
            store.WriteAsync("active-game.json", new { revision }));

        await Task.WhenAll(writes);

        var value = await store.ReadAsync<Dictionary<string, int>>("active-game.json");
        Assert.InRange(value!["revision"], 1, 32);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task WriteAsync_CancellationLeavesNoTemporaryFiles()
    {
        using var directory = new TemporaryDirectory();
        var store = new AtomicJsonStore(directory.Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.WriteAsync("active-game.json", new { revision = 1 }, cancellation.Token));

        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task ReadAsync_ThrowsCancellationBeforeTouchingFile()
    {
        using var directory = new TemporaryDirectory();
        var store = new AtomicJsonStore(directory.Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.ReadAsync<object>("missing.json", cancellation.Token));
    }

    [Fact]
    public async Task ConcurrentReadAndWrite_AlwaysCompletesWithValidJson()
    {
        using var directory = new TemporaryDirectory();
        var store = new AtomicJsonStore(directory.Path);
        await store.WriteAsync("active-game.json", new { revision = 0 });

        var writes = Enumerable.Range(1, 100).Select(i => store.WriteAsync("active-game.json", new { revision = i }));
        var reads = Enumerable.Range(0, 100).Select(_ => store.ReadAsync<Dictionary<string, int>>("active-game.json"));
        await Task.WhenAll(writes.Concat(reads));

        var final = await store.ReadAsync<Dictionary<string, int>>("active-game.json");
        Assert.InRange(final!["revision"], 1, 100);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task DeleteWaitsForSaveAndLeavesSnapshotAbsent()
    {
        using var directory = new TemporaryDirectory();
        var store = new AtomicJsonStore(directory.Path);
        var save = store.WriteAsync("active-game.json", new { revision = 1 });
        await store.DeleteAsync("active-game.json");
        await save;

        Assert.False(File.Exists(Path.Combine(directory.Path, "active-game.json")));
    }
}
