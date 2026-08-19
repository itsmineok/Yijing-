using Yijing.Domain.Board;
using Yijing.Domain.Scoring;

namespace Yijing.Domain.Tests;

public sealed class ChineseAreaScorerTests
{
    [Fact]
    public void Score_CountsLivingStonesAndSurroundedEmptyPoints()
    {
        var state = BoardState.FromSetup(3,
            [
                (new BoardPoint(0, 1), StoneColor.Black),
                (new BoardPoint(1, 0), StoneColor.Black),
                (new BoardPoint(1, 2), StoneColor.Black),
                (new BoardPoint(2, 1), StoneColor.Black)
            ], StoneColor.Black);

        var result = ChineseAreaScorer.Score(state, new HashSet<BoardPoint>(), 7.5);

        Assert.Equal(9, result.BlackArea);
        Assert.Equal(7.5, result.WhiteTotal);
        Assert.Equal(StoneColor.Black, result.Winner);
        Assert.Equal(1.5, result.Margin);
    }

    [Fact]
    public void Score_RemovesMarkedDeadStonesBeforeAreaCounting()
    {
        var dead = new BoardPoint(1, 1);
        var state = BoardState.FromSetup(3,
            [
                (dead, StoneColor.White),
                (new BoardPoint(0, 1), StoneColor.Black),
                (new BoardPoint(1, 0), StoneColor.Black),
                (new BoardPoint(1, 2), StoneColor.Black),
                (new BoardPoint(2, 1), StoneColor.Black)
            ], StoneColor.Black);

        var result = ChineseAreaScorer.Score(state, new HashSet<BoardPoint> { dead }, 7.5);

        Assert.Equal(9, result.BlackArea);
        Assert.Equal(0, result.WhiteArea);
    }
}
