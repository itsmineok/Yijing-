using Yijing.Domain.Board;

namespace Yijing.Domain.Tests;

public sealed class BoardPointTests
{
    [Fact]
    public void IsInside_UsesZeroBasedSquareBounds()
    {
        Assert.True(new BoardPoint(0, 0).IsInside(19));
        Assert.True(new BoardPoint(18, 18).IsInside(19));
        Assert.False(new BoardPoint(-1, 0).IsInside(19));
        Assert.False(new BoardPoint(19, 0).IsInside(19));
    }

    [Fact]
    public void Opponent_SwitchesPlayableColors()
    {
        Assert.Equal(StoneColor.White, StoneColor.Black.Opponent());
        Assert.Equal(StoneColor.Black, StoneColor.White.Opponent());
    }
}
