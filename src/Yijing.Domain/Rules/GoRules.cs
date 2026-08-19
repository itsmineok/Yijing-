using Yijing.Domain.Board;

namespace Yijing.Domain.Rules;

public static class GoRules
{
    public static MoveResult TryApply(BoardState state, Move move)
    {
        if (move.Kind == MoveKind.Pass)
        {
            var passed = BoardState.AfterMove(state, state.CloneCells(),
                state.NextPlayer.Opponent(), state.ConsecutivePasses + 1, state.CloneSeen());
            return MoveResult.Legal(state, passed);
        }

        var point = move.Point;
        if (!point.IsInside(state.Size)) return MoveResult.Illegal(state, IllegalMoveReason.OutsideBoard);
        if (state.At(point) != StoneColor.Empty) return MoveResult.Illegal(state, IllegalMoveReason.Occupied);

        var cells = state.CloneCells();
        var color = state.NextPlayer;
        cells[Index(point, state.Size)] = color;
        var captured = 0;

        foreach (var neighbor in Neighbors(point, state.Size))
        {
            if (cells[Index(neighbor, state.Size)] != color.Opponent()) continue;
            var group = CollectGroup(cells, state.Size, neighbor);
            if (CountLiberties(cells, state.Size, group) != 0) continue;
            foreach (var stone in group) cells[Index(stone, state.Size)] = StoneColor.Empty;
            captured += group.Count;
        }

        var ownGroup = CollectGroup(cells, state.Size, point);
        if (CountLiberties(cells, state.Size, ownGroup) == 0)
            return MoveResult.Illegal(state, IllegalMoveReason.Suicide);

        var key = BoardState.PositionKey(cells);
        if (state.HasSeen(key)) return MoveResult.Illegal(state, IllegalMoveReason.PositionalSuperko);

        var seen = state.CloneSeen();
        seen.Add(key);
        var next = BoardState.AfterMove(state, cells, color.Opponent(), 0, seen);
        return MoveResult.Legal(state, next, captured);
    }

    private static int Index(BoardPoint point, int size) => (point.Row * size) + point.Column;

    private static IEnumerable<BoardPoint> Neighbors(BoardPoint point, int size)
    {
        var candidates = new[]
        {
            new BoardPoint(point.Row - 1, point.Column),
            new BoardPoint(point.Row + 1, point.Column),
            new BoardPoint(point.Row, point.Column - 1),
            new BoardPoint(point.Row, point.Column + 1)
        };
        return candidates.Where(candidate => candidate.IsInside(size));
    }

    private static List<BoardPoint> CollectGroup(StoneColor[] cells, int size, BoardPoint start)
    {
        var color = cells[Index(start, size)];
        var found = new HashSet<BoardPoint> { start };
        var queue = new Queue<BoardPoint>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var point))
        {
            foreach (var neighbor in Neighbors(point, size))
            {
                if (cells[Index(neighbor, size)] == color && found.Add(neighbor)) queue.Enqueue(neighbor);
            }
        }
        return [.. found];
    }

    private static int CountLiberties(StoneColor[] cells, int size, IReadOnlyList<BoardPoint> group)
    {
        var liberties = new HashSet<BoardPoint>();
        foreach (var point in group)
            foreach (var neighbor in Neighbors(point, size))
                if (cells[Index(neighbor, size)] == StoneColor.Empty) liberties.Add(neighbor);
        return liberties.Count;
    }
}
