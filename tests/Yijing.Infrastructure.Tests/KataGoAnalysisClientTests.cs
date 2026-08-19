using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Yijing.Application.Analysis;
using Yijing.Application.Games;
using Yijing.Domain.Board;
using Yijing.Infrastructure.KataGo;

namespace Yijing.Infrastructure.Tests;

public sealed class KataGoAnalysisClientTests
{
    [Fact]
    public async Task AnalyzeAndTerminate_RoutesPartialThenFinalAndWritesExactJson()
    {
        await using var transport = new FakeKataGoTransport();
        await using var client = new KataGoAnalysisClient(transport);
        var position = Position(
            new PlayedMove(StoneColor.Black, Move.Play(new BoardPoint(3, 15))),
            new PlayedMove(StoneColor.White, Move.Pass()));

        var resultsTask = CollectAsync(client.AnalyzeAsync(position, "game-7", CancellationToken.None));
        var request = await transport.ReadWriteAsync();
        await transport.SendAsync(Response("game-7", true, "D4", 0.51));
        await client.TerminateAsync("game-7", CancellationToken.None);
        var terminate = await transport.ReadWriteAsync();
        await transport.SendAsync(Response("game-7", false, "Q16", 0.61));

        var results = await resultsTask;
        Assert.Equal(2, results.Count);
        Assert.False(results[0].IsFinal);
        Assert.True(results[1].IsFinal);
        Assert.Equal("Q16", results[1].Candidates[0].Move);
        Assert.Equal("{\"id\":\"stop-game-7\",\"action\":\"terminate\",\"terminateId\":\"game-7\"}", terminate);

        using var document = JsonDocument.Parse(request);
        var root = document.RootElement;
        Assert.Equal("game-7", root.GetProperty("id").GetString());
        Assert.Equal("B", root.GetProperty("initialPlayer").GetString());
        Assert.Equal("chinese", root.GetProperty("rules").GetString());
        Assert.Equal(7.5, root.GetProperty("komi").GetDouble());
        Assert.Equal(19, root.GetProperty("boardXSize").GetInt32());
        Assert.Equal(19, root.GetProperty("boardYSize").GetInt32());
        Assert.Equal(100_000_000, root.GetProperty("maxVisits").GetInt32());
        Assert.Equal(12, root.GetProperty("analysisPVLen").GetInt32());
        Assert.Equal(1.0, root.GetProperty("reportDuringSearchEvery").GetDouble());
        Assert.True(root.GetProperty("includeOwnership").GetBoolean());
        Assert.Equal("B", root.GetProperty("moves")[0][0].GetString());
        Assert.Equal("Q16", root.GetProperty("moves")[0][1].GetString());
        Assert.Equal("pass", root.GetProperty("moves")[1][1].GetString());
    }

    [Fact]
    public async Task AnalyzeAsync_maps_ownership_for_endgame_suggestions()
    {
        await using var transport = new FakeKataGoTransport();
        await using var client = new KataGoAnalysisClient(transport);
        var results = CollectAsync(client.AnalyzeAsync(Position(), "ownership", CancellationToken.None));
        await transport.ReadWriteAsync();
        await transport.SendAsync(JsonSerializer.Serialize(new
        {
            id = "ownership",
            isDuringSearch = false,
            ownership = new[] { -.96, .99 },
            moveInfos = Array.Empty<object>(),
            rootInfo = new { winrate = .5, scoreLead = 0.0 },
        }));

        Assert.Equal([-.96, .99], (await results).Single().Ownership);
    }

    [Fact]
    public async Task AnalyzeAsync_CorrelatesOutOfOrderResponsesById()
    {
        await using var transport = new FakeKataGoTransport();
        await using var client = new KataGoAnalysisClient(transport);

        var seven = CollectAsync(client.AnalyzeAsync(Position(), "game-7", CancellationToken.None));
        var eight = CollectAsync(client.AnalyzeAsync(Position(), "game-8", CancellationToken.None));
        await transport.ReadWriteAsync();
        await transport.ReadWriteAsync();
        await transport.SendAsync(Response("game-8", false, "C3", 0.8));
        await transport.SendAsync(Response("game-7", false, "D4", 0.7));

        Assert.Equal("D4", (await seven).Single().Candidates.Single().Move);
        Assert.Equal("C3", (await eight).Single().Candidates.Single().Move);
    }

    [Fact]
    public async Task AnalyzeAsync_CancellationUnregistersRequestId()
    {
        await using var transport = new FakeKataGoTransport();
        await using var client = new KataGoAnalysisClient(transport);
        using var cancellation = new CancellationTokenSource();

        var canceled = CollectAsync(client.AnalyzeAsync(Position(), "reusable", cancellation.Token));
        await transport.ReadWriteAsync();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);

        var reused = CollectAsync(client.AnalyzeAsync(Position(), "reusable", CancellationToken.None));
        await transport.ReadWriteAsync();
        await transport.SendAsync(Response("reusable", false, "E5", 0.9));
        Assert.Equal("E5", (await reused).Single().Candidates.Single().Move);
    }

    [Fact]
    public async Task TerminateAsync_BeforeRegistrationCompletesLateRequestWithoutWritingAQuery()
    {
        await using var transport = new FakeKataGoTransport();
        await using var client = new KataGoAnalysisClient(transport);
        await client.TerminateAsync("game-9", CancellationToken.None);
        var terminateLine = await transport.ReadWriteAsync();

        var results = CollectAsync(client.AnalyzeAsync(Position(), "game-9", CancellationToken.None));

        Assert.Contains("terminate", terminateLine, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await results.WaitAsync(TimeSpan.FromSeconds(1)));
        await Assert.ThrowsAsync<TimeoutException>(() =>
            transport.ReadWriteAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(150)));
    }

    [Fact]
    public async Task AnalyzeAsync_ReadFailureCompletesEveryPendingRequest()
    {
        await using var transport = new FakeKataGoTransport();
        await using var client = new KataGoAnalysisClient(transport);
        var first = CollectAsync(client.AnalyzeAsync(Position(), "first", CancellationToken.None));
        var second = CollectAsync(client.AnalyzeAsync(Position(), "second", CancellationToken.None));
        await transport.ReadWriteAsync();
        await transport.ReadWriteAsync();

        transport.FailReads(new IOException("KataGo exited"));

        var one = await Assert.ThrowsAsync<IOException>(() => first);
        var two = await Assert.ThrowsAsync<IOException>(() => second);
        Assert.Equal("KataGo exited", one.Message);
        Assert.Equal("KataGo exited", two.Message);
    }

    [Fact]
    public async Task AnalyzeAsync_WarningDoesNotCompleteRequestBeforeFinalResult()
    {
        await using var transport = new FakeKataGoTransport();
        await using var client = new KataGoAnalysisClient(transport);
        var results = CollectAsync(client.AnalyzeAsync(Position(), "warned", CancellationToken.None));
        await transport.ReadWriteAsync();

        await transport.SendAsync(JsonSerializer.Serialize(new { id = "warned", warning = "rules adjusted" }));
        await transport.SendAsync(Response("warned", false, "Q16", 0.6));

        var result = Assert.Single(await results);
        Assert.True(result.IsFinal);
        Assert.Equal("Q16", result.Candidates.Single().Move);
    }

    [Fact]
    public async Task AnalyzeAsync_GeneralProtocolErrorCompletesAllPendingRequests()
    {
        await using var transport = new FakeKataGoTransport();
        await using var client = new KataGoAnalysisClient(transport);
        var first = CollectAsync(client.AnalyzeAsync(Position(), "first", CancellationToken.None));
        var second = CollectAsync(client.AnalyzeAsync(Position(), "second", CancellationToken.None));
        await transport.ReadWriteAsync();
        await transport.ReadWriteAsync();

        await transport.SendAsync(JsonSerializer.Serialize(new { error = "invalid query" }));

        Assert.Equal("invalid query", (await Assert.ThrowsAsync<InvalidOperationException>(() => first)).Message);
        Assert.Equal("invalid query", (await Assert.ThrowsAsync<InvalidOperationException>(() => second)).Message);
    }

    [Fact]
    public async Task AnalyzeAsync_AfterReadLoopFailureFailsImmediatelyInsteadOfHanging()
    {
        await using var transport = new FakeKataGoTransport();
        await using var client = new KataGoAnalysisClient(transport);
        var first = CollectAsync(client.AnalyzeAsync(Position(), "first", CancellationToken.None));
        await transport.ReadWriteAsync();
        transport.FailReads(new IOException("KataGo exited"));
        await Assert.ThrowsAsync<IOException>(() => first);

        var late = CollectAsync(client.AnalyzeAsync(Position(), "late", CancellationToken.None));

        var error = await Assert.ThrowsAsync<IOException>(() => late.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal("KataGo exited", error.Message);
    }

    [Fact]
    public async Task AnalyzeAsync_UsesRequestedBoardSizeForMoveCoordinates()
    {
        await using var transport = new FakeKataGoTransport();
        await using var client = new KataGoAnalysisClient(transport);
        var position = new AnalysisPosition(
            9,
            StoneColor.White,
            [new PlayedMove(StoneColor.Black, Move.Play(new BoardPoint(0, 0)))],
            7.5,
            1);

        var results = CollectAsync(client.AnalyzeAsync(position, "nine", CancellationToken.None));
        var request = await transport.ReadWriteAsync();
        await transport.SendAsync(Response("nine", false, "D4", 0.5));
        await results;

        using var document = JsonDocument.Parse(request);
        Assert.Equal("A9", document.RootElement.GetProperty("moves")[0][1].GetString());
    }

    private static AnalysisPosition Position(params PlayedMove[] moves) =>
        new(19, moves.Length % 2 == 0 ? StoneColor.Black : StoneColor.White, moves, 7.5, 4);

    private static string Response(string id, bool duringSearch, string move, double winrate) =>
        JsonSerializer.Serialize(new
        {
            id,
            isDuringSearch = duringSearch,
            turnNumber = 2,
            moveInfos = new[] { new { move, winrate, scoreLead = 1.5, visits = 42 } },
            rootInfo = new { winrate, scoreLead = 1.25 }
        });

    private static async Task<IReadOnlyList<AnalysisResult>> CollectAsync(
        IAsyncEnumerable<AnalysisResult> source)
    {
        var results = new List<AnalysisResult>();
        await foreach (var result in source) results.Add(result);
        return results;
    }

    private sealed class FakeKataGoTransport : IKataGoTransport
    {
        private readonly Channel<string> _writes = Channel.CreateUnbounded<string>();
        private readonly Channel<string> _reads = Channel.CreateUnbounded<string>();

        public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken) =>
            _writes.Writer.WriteAsync(line, cancellationToken);

        public IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken) =>
            _reads.Reader.ReadAllAsync(cancellationToken);

        public ValueTask<string> ReadWriteAsync() => _writes.Reader.ReadAsync();
        public ValueTask SendAsync(string line) => _reads.Writer.WriteAsync(line);
        public void FailReads(Exception error) => _reads.Writer.TryComplete(error);

        public ValueTask DisposeAsync()
        {
            _writes.Writer.TryComplete();
            _reads.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
