using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Yijing.Application.Analysis;
using Yijing.Application.Games;
using Yijing.Domain.Board;

namespace Yijing.Application.Tests;

public sealed class AnalysisCoordinatorTests
{
    [Fact]
    public async Task FindAiMoveAsync_AfterDeadlineTerminatesAndReturnsFirstLegalFinalCandidate()
    {
        var analyzer = new FakeAnalyzer();
        var coordinator = new AnalysisCoordinator(analyzer, TimeSpan.FromMilliseconds(25), new ProgressSink());
        var game = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.Black, 7.5));
        game.Play(Move.Play(new BoardPoint(0, 0)));

        var moveTask = coordinator.FindAiMoveAsync(game, CancellationToken.None);
        var request = await analyzer.Requests.Reader.ReadAsync();
        await analyzer.Terminated.Reader.ReadAsync();
        request.Results.Writer.TryWrite(Result(request.Id, false, "A9", "D4"));
        request.Results.Writer.TryComplete();

        var move = await moveTask;
        Assert.Equal(Move.Play(new BoardPoint(5, 3)), move);
        Assert.Single(analyzer.TerminateCalls);
        Assert.Equal(game.Moves, request.Position.Moves);
        Assert.Equal(game.Revision, request.Position.GameRevision);
    }

    [Fact]
    public async Task FindAiMoveAsync_DiscardsProgressAndFinalMoveAfterRevisionChanges()
    {
        var analyzer = new FakeAnalyzer();
        var progress = new ProgressSink();
        var coordinator = new AnalysisCoordinator(analyzer, TimeSpan.FromMilliseconds(25), progress);
        var game = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.Black, 7.5));

        var moveTask = coordinator.FindAiMoveAsync(game, CancellationToken.None);
        var request = await analyzer.Requests.Reader.ReadAsync();
        request.Results.Writer.TryWrite(Result(request.Id, true, "D4"));
        await progress.Published.Reader.ReadAsync();
        game.Play(Move.Pass());
        request.Results.Writer.TryWrite(Result(request.Id, true, "E5"));
        await analyzer.Terminated.Reader.ReadAsync();
        request.Results.Writer.TryWrite(Result(request.Id, false, "F6"));
        request.Results.Writer.TryComplete();

        Assert.Null(await moveTask);
        Assert.Single(progress.Items);
        Assert.DoesNotContain(progress.Items, result => result.Candidates.Any(candidate => candidate.Move == "E5"));
    }

    [Fact]
    public async Task FindAiMoveAsync_RevisionChangeTerminatesBeforeLongDeadlineAndWaitsForFinal()
    {
        var analyzer = new FakeAnalyzer();
        var coordinator = new AnalysisCoordinator(analyzer, TimeSpan.FromMinutes(1), new ProgressSink());
        var game = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.Black, 7.5));

        var moveTask = coordinator.FindAiMoveAsync(game, CancellationToken.None);
        var request = await analyzer.Requests.Reader.ReadAsync();
        game.Play(Move.Pass());

        await analyzer.Terminated.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(moveTask.IsCompleted);
        request.Results.Writer.TryWrite(Result(request.Id, false, "D4"));

        Assert.Null(await moveTask.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Single(analyzer.TerminateCalls);
    }

    [Fact]
    public async Task FindAiMoveAsync_CancellationTerminatesAndWaitsForFinalResponse()
    {
        var analyzer = new FakeAnalyzer();
        var coordinator = new AnalysisCoordinator(analyzer, TimeSpan.FromMinutes(1), new ProgressSink());
        var game = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.Black, 7.5));
        using var cancellation = new CancellationTokenSource();

        var moveTask = coordinator.FindAiMoveAsync(game, cancellation.Token);
        var request = await analyzer.Requests.Reader.ReadAsync();
        cancellation.Cancel();
        await analyzer.Terminated.Reader.ReadAsync();
        Assert.False(moveTask.IsCompleted);
        request.Results.Writer.TryWrite(Result(request.Id, false, "pass"));
        request.Results.Writer.TryComplete();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => moveTask);
        Assert.Single(analyzer.TerminateCalls);
    }

    [Fact]
    public async Task FindAiMoveAsync_CompletesWhenFinalArrivesWithoutWaitingForStreamClosure()
    {
        var analyzer = new FakeAnalyzer();
        var coordinator = new AnalysisCoordinator(analyzer, TimeSpan.FromMilliseconds(25), new ProgressSink());
        var game = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.Black, 7.5));

        var moveTask = coordinator.FindAiMoveAsync(game, CancellationToken.None);
        var request = await analyzer.Requests.Reader.ReadAsync();
        await analyzer.Terminated.Reader.ReadAsync();
        request.Results.Writer.TryWrite(Result(request.Id, false, "pass"));

        Assert.Equal(Move.Pass(), await moveTask.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task FindAiMoveAsync_UsesDurationReturnedForCurrentMoveCount()
    {
        var analyzer = new FakeAnalyzer();
        var requestedMoves = new List<int>();
        var coordinator = new AnalysisCoordinator(
            analyzer,
            moves =>
            {
                requestedMoves.Add(moves);
                return TimeSpan.FromMilliseconds(25);
            },
            new ProgressSink());
        var game = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.Black, 7.5));
        game.Play(Move.Play(new BoardPoint(0, 0)));
        game.Play(Move.Play(new BoardPoint(0, 1)));

        var moveTask = coordinator.FindAiMoveAsync(game, CancellationToken.None);
        var request = await analyzer.Requests.Reader.ReadAsync();
        await analyzer.Terminated.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        request.Results.Writer.TryWrite(Result(request.Id, false, "A9", "D4"));
        request.Results.Writer.TryComplete();

        var move = await moveTask;
        Assert.Equal(Move.Play(new BoardPoint(5, 3)), move);
        Assert.Contains(2, requestedMoves);
    }

    [Fact]
    public async Task FindAiMoveAsync_FunctionOverloadHonorsReturnedDuration()
    {
        var analyzer = new FakeAnalyzer();
        var requestedMoves = new List<int>();
        var coordinator = new AnalysisCoordinator(
            analyzer,
            moves =>
            {
                requestedMoves.Add(moves);
                return TimeSpan.FromMinutes(1);
            },
            new ProgressSink());
        var game = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.Black, 7.5));

        var moveTask = coordinator.FindAiMoveAsync(game, CancellationToken.None);
        var request = await analyzer.Requests.Reader.ReadAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.DoesNotContain(requestedMoves, move => move != 0);
        Assert.Equal(0, analyzer.Terminated.Reader.Count);
        Assert.False(moveTask.IsCompleted);
        request.Results.Writer.TryWrite(Result(request.Id, false, "pass"));
        request.Results.Writer.TryComplete();

        Assert.Equal(Move.Pass(), await moveTask.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal([0], requestedMoves);
    }

    private static AnalysisResult Result(string id, bool duringSearch, params string[] moves) =>
        new(id, !duringSearch, moves.Select(move => new CandidateMove(move, 0.6, 1.0, 10)).ToArray(), 0.6, 1.0);

    private sealed class ProgressSink : IProgress<AnalysisResult>
    {
        public List<AnalysisResult> Items { get; } = [];
        public Channel<AnalysisResult> Published { get; } = Channel.CreateUnbounded<AnalysisResult>();

        public void Report(AnalysisResult value)
        {
            Items.Add(value);
            Published.Writer.TryWrite(value);
        }
    }

    private sealed class FakeAnalyzer : IPositionAnalyzer
    {
        public Channel<Request> Requests { get; } = Channel.CreateUnbounded<Request>();
        public Channel<string> Terminated { get; } = Channel.CreateUnbounded<string>();
        public ConcurrentQueue<string> TerminateCalls { get; } = new();

        public async IAsyncEnumerable<AnalysisResult> AnalyzeAsync(
            AnalysisPosition position,
            string requestId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var request = new Request(position, requestId);
            await Requests.Writer.WriteAsync(request, cancellationToken);
            await foreach (var result in request.Results.Reader.ReadAllAsync(cancellationToken)) yield return result;
        }

        public Task TerminateAsync(string requestId, CancellationToken cancellationToken)
        {
            TerminateCalls.Enqueue(requestId);
            return Terminated.Writer.WriteAsync(requestId, cancellationToken).AsTask();
        }
    }

    private sealed record Request(AnalysisPosition Position, string Id)
    {
        public Channel<AnalysisResult> Results { get; } = Channel.CreateUnbounded<AnalysisResult>();
    }
}
