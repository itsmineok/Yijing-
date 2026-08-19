using System.Runtime.CompilerServices;
using Yijing.Application.Analysis;
using Yijing.Desktop.Services;
using Yijing.Domain.Board;

namespace Yijing.Desktop.Tests;

public sealed class SwitchablePositionAnalyzerSwitchTests
{
    [Fact]
    public async Task TrySwitchAsync_DisposesPreviousAndRoutesNewRequests()
    {
        await using var target = new SwitchablePositionAnalyzer();
        var first = new FakeAnalyzer();
        Assert.True(target.TryConfigure(first));
        var second = new FakeAnalyzer();

        var switched = await target.TrySwitchAsync(second);

        Assert.True(switched);
        Assert.True(first.IsDisposed);

        await using var enumeration = target.AnalyzeAsync(
            new AnalysisPosition(19, StoneColor.Black, [], 7.5, 1),
            "request-1",
            CancellationToken.None).GetAsyncEnumerator();
        await enumeration.MoveNextAsync();

        Assert.Equal(1, second.RequestCount);
        Assert.Equal(0, first.RequestCount);
    }

    [Fact]
    public async Task TerminateAsync_AfterSwitchRoutesToNewAnalyzer()
    {
        await using var target = new SwitchablePositionAnalyzer();
        var first = new FakeAnalyzer();
        Assert.True(target.TryConfigure(first));
        var second = new FakeAnalyzer();
        await target.TrySwitchAsync(second);

        await target.TerminateAsync("request-9", CancellationToken.None);

        Assert.Equal(["request-9"], second.Terminated);
        Assert.Empty(first.Terminated);
    }

    [Fact]
    public async Task TrySwitchAsync_RecoversAfterUnavailable()
    {
        await using var target = new SwitchablePositionAnalyzer();
        Assert.True(target.MarkUnavailable(new InvalidOperationException("engine down")));
        var replacement = new FakeAnalyzer();

        var switched = await target.TrySwitchAsync(replacement);

        Assert.True(switched);
        await using var enumeration = target.AnalyzeAsync(
            new AnalysisPosition(19, StoneColor.Black, [], 7.5, 1),
            "request-2",
            CancellationToken.None).GetAsyncEnumerator();
        await enumeration.MoveNextAsync();
        Assert.Equal(1, replacement.RequestCount);
    }

    [Fact]
    public async Task TrySwitchAsync_ReturnsFalseAfterDispose()
    {
        await using var target = new SwitchablePositionAnalyzer();
        Assert.True(target.TryConfigure(new FakeAnalyzer()));
        await target.DisposeAsync();

        Assert.False(await target.TrySwitchAsync(new FakeAnalyzer()));
    }

    private sealed class FakeAnalyzer : IPositionAnalyzer, IAsyncDisposable
    {
        public int RequestCount { get; private set; }

        public List<string> Terminated { get; } = [];

        public bool IsDisposed { get; private set; }

        public async IAsyncEnumerable<AnalysisResult> AnalyzeAsync(
            AnalysisPosition position,
            string requestId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            RequestCount++;
            await Task.CompletedTask;
            yield break;
        }

        public Task TerminateAsync(string requestId, CancellationToken cancellationToken)
        {
            Terminated.Add(requestId);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
