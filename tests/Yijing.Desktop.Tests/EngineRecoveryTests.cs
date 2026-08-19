using System.Threading.Channels;
using Yijing.Application.Analysis;
using Yijing.Application.Games;
using Yijing.Desktop.Services;
using Yijing.Domain.Board;
using Yijing.Infrastructure.KataGo;

namespace Yijing.Desktop.Tests;

public sealed class EngineRecoveryTests
{
    [Fact]
    public async Task Pending_detection_request_can_be_terminated_before_engine_is_configured()
    {
        await using var analyzer = new SwitchablePositionAnalyzer();
        var position = new AnalysisPosition(9, StoneColor.Black, [], 7.5, 0);
        var pending = Task.Run(async () =>
        {
            await foreach (var _ in analyzer.AnalyzeAsync(position, "pending", CancellationToken.None)) { }
        });
        await Task.Delay(30);

        await analyzer.TerminateAsync("pending", CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Unexpected_exit_saves_restarts_once_and_replays_the_same_full_position()
    {
        var broken = new ThrowingAnalyzer();
        var replacement = new RecordingAnalyzer();
        var saves = 0;
        var restarts = 0;
        await using var analyzer = new RestartingPositionAnalyzer(
            broken,
            _ =>
            {
                restarts++;
                return Task.FromResult<IPositionAnalyzer>(replacement);
            },
            _ => { saves++; return Task.CompletedTask; });
        var moves = new[] { new PlayedMove(StoneColor.Black, Move.Play(new BoardPoint(0, 0))) };
        var position = new AnalysisPosition(9, StoneColor.White, moves, 7.5, 1);

        var results = new List<AnalysisResult>();
        await foreach (var result in analyzer.AnalyzeAsync(position, "request-1", CancellationToken.None))
            results.Add(result);

        Assert.Equal(1, saves);
        Assert.Equal(1, restarts);
        Assert.Same(position, replacement.LastPosition);
        Assert.Single(results);
    }

    [Fact]
    public async Task Second_exit_disables_ai_for_the_session()
    {
        var unavailable = 0;
        await using var analyzer = new RestartingPositionAnalyzer(
            new ThrowingAnalyzer(),
            _ => Task.FromResult<IPositionAnalyzer>(new ThrowingAnalyzer()),
            _ => Task.CompletedTask,
            _ => unavailable++);
        var position = new AnalysisPosition(9, StoneColor.Black, [], 7.5, 0);

        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var _ in analyzer.AnalyzeAsync(position, "request-2", CancellationToken.None)) { }
        });

        Assert.Equal(1, unavailable);
        Assert.True(analyzer.IsDisabled);
    }

    [Fact]
    public async Task Terminate_during_restart_cancels_resubmission_without_throwing()
    {
        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacement = new RecordingAnalyzer();
        await using var analyzer = new RestartingPositionAnalyzer(
            new CrashingAnalyzer(),
            async _ =>
            {
                factoryEntered.TrySetResult();
                await releaseFactory.Task;
                return (IPositionAnalyzer)replacement;
            },
            _ => Task.CompletedTask);
        var position = new AnalysisPosition(9, StoneColor.Black, [], 7.5, 0);

        var enumeration = Task.Run(async () =>
        {
            var results = new List<AnalysisResult>();
            await foreach (var result in analyzer.AnalyzeAsync(position, "request-3", CancellationToken.None))
                results.Add(result);
            return results;
        });

        await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        try
        {
            await analyzer.TerminateAsync("request-3", CancellationToken.None);
        }
        finally
        {
            releaseFactory.TrySetResult();
        }

        var results = await enumeration.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Empty(results);
        Assert.Null(replacement.LastPosition);
    }

    [Fact]
    public async Task Terminate_while_engine_restarts_never_hangs_or_throws()
    {
        await using var transportA = new FakeTransport();
        var clientA = new KataGoAnalysisClient(transportA);
        await using var transportB = new FakeTransport();
        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        KataGoAnalysisClient? clientB = null;
        await using var analyzer = new RestartingPositionAnalyzer(
            clientA,
            async _ =>
            {
                clientB = new KataGoAnalysisClient(transportB);
                factoryEntered.TrySetResult();
                await releaseFactory.Task;
                return (IPositionAnalyzer)clientB;
            },
            _ => Task.CompletedTask);
        var position = new AnalysisPosition(9, StoneColor.Black, [], 7.5, 0);

        var enumeration = Task.Run(async () =>
        {
            var results = new List<AnalysisResult>();
            await foreach (var result in analyzer.AnalyzeAsync(position, "request-4", CancellationToken.None))
                results.Add(result);
            return results;
        });

        await transportA.ReadWriteAsync();
        transportA.FailReads(new IOException("engine exited"));
        await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        try
        {
            await analyzer.TerminateAsync("request-4", CancellationToken.None);
        }
        finally
        {
            releaseFactory.TrySetResult();
        }

        var results = await enumeration.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(results);
        await Assert.ThrowsAsync<TimeoutException>(() =>
            transportB.ReadWriteAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(150)));
        await clientB!.DisposeAsync();
    }

    private sealed class ThrowingAnalyzer : IPositionAnalyzer, IAsyncDisposable
    {
        public async IAsyncEnumerable<AnalysisResult> AnalyzeAsync(
            AnalysisPosition position,
            string requestId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new IOException("engine exited");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
        public Task TerminateAsync(string requestId, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CrashingAnalyzer : IPositionAnalyzer, IAsyncDisposable
    {
        public async IAsyncEnumerable<AnalysisResult> AnalyzeAsync(
            AnalysisPosition position,
            string requestId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new IOException("engine exited");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
        public Task TerminateAsync(string requestId, CancellationToken cancellationToken) =>
            throw new ObjectDisposedException(nameof(CrashingAnalyzer));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeTransport : IKataGoTransport
    {
        private readonly Channel<string> _writes = Channel.CreateUnbounded<string>();
        private readonly Channel<string> _reads = Channel.CreateUnbounded<string>();

        public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken) =>
            _writes.Writer.WriteAsync(line, cancellationToken);

        public IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken) =>
            _reads.Reader.ReadAllAsync(cancellationToken);

        public ValueTask<string> ReadWriteAsync() => _writes.Reader.ReadAsync();
        public void FailReads(Exception error) => _reads.Writer.TryComplete(error);

        public ValueTask DisposeAsync()
        {
            _writes.Writer.TryComplete();
            _reads.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingAnalyzer : IPositionAnalyzer, IAsyncDisposable
    {
        public AnalysisPosition? LastPosition { get; private set; }
        public async IAsyncEnumerable<AnalysisResult> AnalyzeAsync(
            AnalysisPosition position,
            string requestId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastPosition = position;
            yield return new AnalysisResult(requestId, true, [], .5, 0);
            await Task.CompletedTask;
        }
        public Task TerminateAsync(string requestId, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
