using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using Yijing.Application.Analysis;

namespace Yijing.Desktop.Services;

/// <summary>
/// Owns the replaceable KataGo client for a single game session. A retry submits
/// the unchanged AnalysisPosition, whose Moves collection is the complete history.
/// </summary>
public sealed class RestartingPositionAnalyzer : IPositionAnalyzer, IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<IPositionAnalyzer>> _restartFactory;
    private readonly Func<CancellationToken, Task> _saveBeforeRestart;
    private readonly Action<Exception>? _disabled;
    private readonly Action<Exception, int, string>? _failureObserved;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _terminateIntents = new(StringComparer.Ordinal);
    private IPositionAnalyzer _current;
    private int _failureCount;
    private int _disposed;

    public RestartingPositionAnalyzer(
        IPositionAnalyzer initial,
        Func<CancellationToken, Task<IPositionAnalyzer>> restartFactory,
        Func<CancellationToken, Task> saveBeforeRestart,
        Action<Exception>? disabled = null,
        Action<Exception, int, string>? failureObserved = null)
    {
        _current = initial ?? throw new ArgumentNullException(nameof(initial));
        _restartFactory = restartFactory ?? throw new ArgumentNullException(nameof(restartFactory));
        _saveBeforeRestart = saveBeforeRestart ?? throw new ArgumentNullException(nameof(saveBeforeRestart));
        _disabled = disabled;
        _failureObserved = failureObserved;
    }

    public bool IsDisabled { get; private set; }

    public void ResetSession()
    {
        Interlocked.Exchange(ref _failureCount, 0);
        IsDisabled = false;
    }

    public async IAsyncEnumerable<AnalysisResult> AnalyzeAsync(
        AnalysisPosition position,
        string requestId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(position);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (_terminateIntents.ContainsKey(requestId))
                    yield break;

                var analyzer = _current;
                await using var enumerator = analyzer.AnalyzeAsync(position, requestId, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);
                while (true)
                {
                    Exception? failure = null;
                    bool hasNext = false;
                    try
                    {
                        hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }

                    if (failure is null)
                    {
                        if (!hasNext) yield break;
                        yield return enumerator.Current;
                        continue;
                    }

                    var failureCount = Interlocked.Increment(ref _failureCount);
                    _failureObserved?.Invoke(failure, failureCount, requestId);
                    if (failureCount > 1)
                    {
                        IsDisabled = true;
                        _disabled?.Invoke(failure);
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                    }

                    await _saveBeforeRestart(CancellationToken.None).ConfigureAwait(false);
                    await RestartAsync(analyzer, cancellationToken).ConfigureAwait(false);
                    break;
                }
            }
        }
        finally
        {
            _terminateIntents.TryRemove(requestId, out _);
        }
    }

    public async Task TerminateAsync(string requestId, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _terminateIntents.TryAdd(requestId, 0);
        try
        {
            await _current.TerminateAsync(requestId, cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The analyzer is being replaced by a restart; the recorded intent
            // cancels the resubmitted request when the retry begins.
        }
        catch (IOException)
        {
            // The engine process died; the restart path consumes the intent.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisposeAnalyzerAsync(_current).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
        }
    }

    private async Task RestartAsync(IPositionAnalyzer failed, CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(_current, failed))
            {
                await DisposeAnalyzerAsync(failed).ConfigureAwait(false);
                _current = await _restartFactory(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private static async ValueTask DisposeAnalyzerAsync(IPositionAnalyzer analyzer)
    {
        if (analyzer is IAsyncDisposable disposable) await disposable.DisposeAsync().ConfigureAwait(false);
        else if (analyzer is IDisposable synchronous) synchronous.Dispose();
    }
}
