namespace Yijing.Domain.Board;

public enum StoneColor { Empty = 0, Black = 1, White = 2 }

public static class StoneColorExtensions
{
    public static StoneColor Opponent(this StoneColor color) => color switch
    {
        StoneColor.Black => StoneColor.White,
        StoneColor.White => StoneColor.Black,
        _ => throw new ArgumentOutOfRangeException(nameof(color), color, "空点没有对手色。")
    };
}
