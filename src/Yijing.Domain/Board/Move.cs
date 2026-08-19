namespace Yijing.Domain.Board;

public enum MoveKind { Play, Pass }

public readonly record struct Move(MoveKind Kind, BoardPoint Point)
{
    public static Move Play(BoardPoint point) => new(MoveKind.Play, point);
    public static Move Pass() => new(MoveKind.Pass, default);
}
