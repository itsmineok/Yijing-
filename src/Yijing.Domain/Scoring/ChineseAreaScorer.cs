using Yijing.Domain.Board;

namespace Yijing.Domain.Scoring;

public static class ChineseAreaScorer
{
    public static ScoreResult Score(BoardState state, IReadOnlySet<BoardPoint> deadStones, double komi)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(deadStones);

        var cells = state.CloneCells();
        foreach (var dead in deadStones)
        {
            if (dead.IsInside(state.Size))
                cells[Index(dead, state.Size)] = StoneColor.Empty;
        }

        var blackArea = 0;
        var whiteArea = 0;
        for (var row = 0; row < state.Size; row++)
        {
            for (var column = 0; column < state.Size; column++)
            {
                var point = new BoardPoint(row, column);
                var color = cells[Index(point, state.Size)];
                if (color == StoneColor.Black)
                {
                    blackArea++;
                }
                else if (color == StoneColor.White)
                {
                    whiteArea++;
                }
            }
        }

        var visited = new HashSet<BoardPoint>();
        for (var row = 0; row < state.Size; row++)
        {
            for (var column = 0; column < state.Size; column++)
            {
                var start = new BoardPoint(row, column);
                if (cells[Index(start, state.Size)] != StoneColor.Empty || !visited.Add(start))
                    continue;

                var region = new List<BoardPoint> { start };
                var borderingColors = new HashSet<StoneColor>();
                var queue = new Queue<BoardPoint>();
                queue.Enqueue(start);
                while (queue.TryDequeue(out var point))
                {
                    foreach (var neighbor in Neighbors(point, state.Size))
                    {
                        var neighborColor = cells[Index(neighbor, state.Size)];
                        if (neighborColor == StoneColor.Empty)
                        {
                            if (visited.Add(neighbor))
                            {
                                region.Add(neighbor);
                                queue.Enqueue(neighbor);
                            }
                        }
                        else
                        {
                            borderingColors.Add(neighborColor);
                        }
                    }
                }

                if (borderingColors.Count == 1)
                {
                    if (borderingColors.Contains(StoneColor.Black)) blackArea += region.Count;
                    else if (borderingColors.Contains(StoneColor.White)) whiteArea += region.Count;
                }
            }
        }

        var whiteTotal = whiteArea + komi;
        var winner = blackArea > whiteTotal ? StoneColor.Black : StoneColor.White;
        var margin = Math.Abs(blackArea - whiteTotal);
        return new ScoreResult(blackArea, whiteArea, komi, winner, margin);
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
}
