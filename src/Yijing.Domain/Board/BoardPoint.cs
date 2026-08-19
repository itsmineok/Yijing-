namespace Yijing.Domain.Board;

public readonly record struct BoardPoint(int Row, int Column)
{
    public bool IsInside(int boardSize) =>
        boardSize is >= 2 and <= 19 && Row >= 0 && Row < boardSize && Column >= 0 && Column < boardSize;
}
