using System.Text.Json;
using System.Collections.Concurrent;

namespace Yijing.Infrastructure.Storage;

/// <summary>Small atomic JSON file store used for crash-safe application state.</summary>
public sealed class AtomicJsonStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _pathLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _directory;

    public AtomicJsonStore(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        _directory = directory;
    }

    public async Task WriteAsync<T>(string name, T value, CancellationToken cancellationToken = default)
    {
        var target = GetPath(name);
        var gate = _pathLocks.GetOrAdd(target, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(_directory);
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            await MoveIntoPlaceAsync(temporary, target, cancellationToken);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            gate.Release();
        }
    }

    private static async Task MoveIntoPlaceAsync(
        string temporary,
        string target,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temporary, target, overwrite: true);
                return;
            }
            catch (Exception exception) when (
                attempt < 5 && exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10 * (attempt + 1)), cancellationToken);
            }
        }
    }

    public async Task<T?> ReadAsync<T>(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(name);
        if (!File.Exists(path)) return default;
        var gate = _pathLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return default;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(name);
        var gate = _pathLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                File.Delete(path);
            }
            catch (FileNotFoundException)
            {
                // Deleting a missing snapshot is idempotent.
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private string GetPath(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (Path.GetFileName(name) != name) throw new ArgumentException("File name must not contain a directory.", nameof(name));
        return Path.Combine(_directory, name);
    }
}
