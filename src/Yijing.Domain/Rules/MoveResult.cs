using Yijing.Domain.Board;

namespace Yijing.Domain.Rules;

public enum IllegalMoveReason { OutsideBoard, Occupied, Suicide, PositionalSuperko, GameFinished }

public sealed record MoveResult(
    BoardState OriginalState,
    BoardState? State,
    IllegalMoveReason? IllegalReason,
    int CapturedStones)
{
    public bool IsLegal => State is not null;
    public static MoveResult Legal(BoardState original, BoardState next, int captured = 0) =>
        new(original, next, null, captured);
    public static MoveResult Illegal(BoardState original, IllegalMoveReason reason) =>
        new(original, null, reason, 0);
}
