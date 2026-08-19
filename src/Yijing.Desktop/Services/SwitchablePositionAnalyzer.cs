using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using Yijing.Application.Analysis;

namespace Yijing.Desktop.Services;

public sealed class SwitchablePositionAnalyzer : IPositionAnalyzer, IAsyncDisposable
{
    private TaskCompletionSource<IPositionAnalyzer> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IPositionAnalyzer? _configured;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _terminatedBeforeReady = new(StringComparer.Ordinal);
    private int _disposed;

    public bool IsConfigured => _configured is not null;

    public bool TryConfigure(IPositionAnalyzer analyzer)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        if (Volatile.Read(ref _disposed) != 0) return false;
        if (Interlocked.CompareExchange(ref _configured, analyzer, null) is not null) return false;
        Volatile.Read(ref _ready).TrySetResult(analyzer);
        return true;
    }

    public bool MarkUnavailable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Volatile.Read(ref _disposed) != 0) return false;
        return Volatile.Read(ref _ready).TrySetException(exception);
    }

    public async Task<bool> TrySwitchAsync(IPositionAnalyzer analyzer)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        if (Volatile.Read(ref _disposed) != 0) return false;
        var ready = Volatile.Read(ref _ready);
        if (ready.Task.IsFaulted)
        {
            var replacement = new TaskCompletionSource<IPositionAnalyzer>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _ready, replacement, ready), ready))
                ready = replacement;
            else
                ready = Volatile.Read(ref _ready);
        }
        var previous = Interlocked.Exchange(ref _configured, analyzer);
        ready.TrySetResult(analyzer);
        if (previous is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (previous is IDisposable disposable)
            disposable.Dispose();
        return true;
    }

    public void ResetSession()
    {
        if (_configured is RestartingPositionAnalyzer recovering) recovering.ResetSession();
    }

    public async IAsyncEnumerable<AnalysisResult> AnalyzeAsync(
        AnalysisPosition position,
        string requestId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var pendingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!_pending.TryAdd(requestId, pendingCancellation))
            throw new InvalidOperationException($"An analysis request named '{requestId}' is already active.");
        if (_terminatedBeforeReady.TryRemove(requestId, out _)) pendingCancellation.Cancel();
        try
        {
            await Volatile.Read(ref _ready).Task.WaitAsync(pendingCancellation.Token).ConfigureAwait(false);
            var analyzer = Volatile.Read(ref _configured)!;
            await foreach (var result in analyzer.AnalyzeAsync(position, requestId, pendingCancellation.Token)
                               .ConfigureAwait(false))
                yield return result;
        }
        finally
        {
            _pending.TryRemove(new KeyValuePair<string, CancellationTokenSource>(requestId, pendingCancellation));
        }
    }

    public async Task TerminateAsync(string requestId, CancellationToken cancellationToken)
    {
        if (!Volatile.Read(ref _ready).Task.IsCompletedSuccessfully)
        {
            if (_pending.TryGetValue(requestId, out var pending)) pending.Cancel();
            else _terminatedBeforeReady.TryAdd(requestId, 0);
            return;
        }
        var configured = Volatile.Read(ref _configured)!;
        await configured.TerminateAsync(requestId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Volatile.Read(ref _ready).TrySetException(new ObjectDisposedException(nameof(SwitchablePositionAnalyzer)));
        if (_configured is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (_configured is IDisposable disposable)
            disposable.Dispose();
    }
}
