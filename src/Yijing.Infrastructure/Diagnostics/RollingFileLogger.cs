using System.Text;
using System.Text.Json;

namespace Yijing.Infrastructure.Diagnostics;

public sealed record EngineLogEntry(
    string EngineVersion,
    string Backend,
    int? ExitCode,
    string? RequestId,
    long ElapsedMilliseconds,
    string? ExceptionType);

public sealed class RollingFileLogger
{
    private const long DefaultMaxBytes = 5L * 1024 * 1024;
    private readonly string _directory;
    private readonly string _activePath;
    private readonly long _maxBytes;
    private readonly int _retainedFiles;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _sequence;

    public RollingFileLogger(
        string? logDirectory = null,
        long maxBytes = DefaultMaxBytes,
        int retainedFiles = 7)
    {
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (retainedFiles < 0) throw new ArgumentOutOfRangeException(nameof(retainedFiles));
        _directory = Path.GetFullPath(logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Yijing", "logs"));
        _activePath = Path.Combine(_directory, "yijing.log");
        _maxBytes = maxBytes;
        _retainedFiles = retainedFiles;
        Directory.CreateDirectory(_directory);
        PruneArchives();
    }

    public async Task WriteEngineAsync(
        EngineLogEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var payload = JsonSerializer.Serialize(new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            engineVersion = entry.EngineVersion,
            backend = entry.Backend,
            exitCode = entry.ExitCode,
            requestId = entry.RequestId,
            elapsedMilliseconds = entry.ElapsedMilliseconds,
            exceptionType = entry.ExceptionType,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)) + Environment.NewLine;
        var bytes = Encoding.UTF8.GetByteCount(payload);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_activePath) && new FileInfo(_activePath).Length + bytes > _maxBytes)
                Rotate();
            await File.AppendAllTextAsync(_activePath, payload, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Rotate()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
        var archive = Path.Combine(_directory, $"yijing-{stamp}-{Interlocked.Increment(ref _sequence):D4}.log");
        File.Move(_activePath, archive);
        PruneArchives();
    }

    private void PruneArchives()
    {
        var archives = Directory.EnumerateFiles(_directory, "yijing-*.log")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .Skip(_retainedFiles)
            .ToArray();
        foreach (var archive in archives)
        {
            try { archive.Delete(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
