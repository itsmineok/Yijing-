using Yijing.Domain.Board;
using Yijing.Domain.Rules;

namespace Yijing.Domain.Tests;

public sealed class GoRulesTests
{
    [Fact]
    public void Play_CapturesOpponentGroupWithoutLiberties()
    {
        var state = BoardState.FromSetup(5,
            [
                (new BoardPoint(1, 1), StoneColor.White),
                (new BoardPoint(0, 1), StoneColor.Black),
                (new BoardPoint(1, 0), StoneColor.Black),
                (new BoardPoint(2, 1), StoneColor.Black)
            ], StoneColor.Black);

        var result = GoRules.TryApply(state, Move.Play(new BoardPoint(1, 2)));

        Assert.True(result.IsLegal);
        Assert.Equal(StoneColor.Empty, result.State!.At(new BoardPoint(1, 1)));
        Assert.Equal(1, result.CapturedStones);
        Assert.Equal(StoneColor.White, result.State.NextPlayer);
    }

    [Fact]
    public void Play_RejectsSuicideWithoutChangingState()
    {
        var state = BoardState.FromSetup(3,
            [
                (new BoardPoint(0, 1), StoneColor.White),
                (new BoardPoint(1, 0), StoneColor.White),
                (new BoardPoint(1, 2), StoneColor.White),
                (new BoardPoint(2, 1), StoneColor.White)
            ], StoneColor.Black);

        var result = GoRules.TryApply(state, Move.Play(new BoardPoint(1, 1)));

        Assert.False(result.IsLegal);
        Assert.Equal(IllegalMoveReason.Suicide, result.IllegalReason);
        Assert.Same(state, result.OriginalState);
    }

    [Fact]
    public void Pass_TwiceMarksEndByConsecutivePasses()
    {
        var first = GoRules.TryApply(BoardState.Empty(9), Move.Pass()).State!;
        var second = GoRules.TryApply(first, Move.Pass()).State!;

        Assert.Equal(2, second.ConsecutivePasses);
        Assert.True(second.HasTwoConsecutivePasses);
    }

    [Fact]
    public void Play_RejectsImmediateKoRecaptureByPositionalSuperko()
    {
        var state = BoardState.FromSetup(5,
            [
                (new BoardPoint(0, 1), StoneColor.Black),
                (new BoardPoint(1, 0), StoneColor.Black),
                (new BoardPoint(2, 1), StoneColor.Black),
                (new BoardPoint(1, 1), StoneColor.White),
                (new BoardPoint(0, 2), StoneColor.White),
                (new BoardPoint(2, 2), StoneColor.White),
                (new BoardPoint(1, 3), StoneColor.White)
            ], StoneColor.Black);

        var capture = GoRules.TryApply(state, Move.Play(new BoardPoint(1, 2))).State!;
        var recapture = GoRules.TryApply(capture, Move.Play(new BoardPoint(1, 1)));

        Assert.False(recapture.IsLegal);
        Assert.Equal(IllegalMoveReason.PositionalSuperko, recapture.IllegalReason);
    }
}
