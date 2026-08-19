namespace Yijing.Domain.Board;

public sealed class BoardState
{
    private readonly StoneColor[] _cells;
    private readonly HashSet<string> _seenPositionKeys;

    private BoardState(int size, StoneColor[] cells, StoneColor nextPlayer,
        int consecutivePasses, int moveNumber, HashSet<string> seenPositionKeys)
    {
        Size = size;
        _cells = cells;
        NextPlayer = nextPlayer;
        ConsecutivePasses = consecutivePasses;
        MoveNumber = moveNumber;
        _seenPositionKeys = seenPositionKeys;
    }

    public int Size { get; }
    public StoneColor NextPlayer { get; }
    public int ConsecutivePasses { get; }
    public int MoveNumber { get; }
    public bool HasTwoConsecutivePasses => ConsecutivePasses >= 2;

    public StoneColor At(BoardPoint point)
    {
        if (!point.IsInside(Size)) throw new ArgumentOutOfRangeException(nameof(point));
        return _cells[(point.Row * Size) + point.Column];
    }

    public static BoardState Empty(int size) => FromSetup(size, [], StoneColor.Black);

    public static BoardState FromSetup(int size,
        IEnumerable<(BoardPoint Point, StoneColor Color)> stones,
        StoneColor nextPlayer)
    {
        if (size is < 2 or > 19)
            throw new ArgumentOutOfRangeException(nameof(size));
        if (nextPlayer == StoneColor.Empty)
            throw new ArgumentOutOfRangeException(nameof(nextPlayer));

        var cells = new StoneColor[size * size];
        foreach (var (point, color) in stones)
        {
            if (!point.IsInside(size) || color == StoneColor.Empty)
                throw new ArgumentException("初始棋子无效。", nameof(stones));
            var index = (point.Row * size) + point.Column;
            if (cells[index] != StoneColor.Empty)
                throw new ArgumentException("初始棋子坐标重复。", nameof(stones));
            cells[index] = color;
        }

        var key = PositionKey(cells);
        return new BoardState(size, cells, nextPlayer, 0, 0, new HashSet<string> { key });
    }

    internal StoneColor[] CloneCells() => (StoneColor[])_cells.Clone();
    internal bool HasSeen(string key) => _seenPositionKeys.Contains(key);
    internal HashSet<string> CloneSeen() => new(_seenPositionKeys, StringComparer.Ordinal);
    internal static string PositionKey(StoneColor[] cells) =>
        string.Create(cells.Length, cells, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = source[i] switch { StoneColor.Black => 'B', StoneColor.White => 'W', _ => '.' };
        });

    internal static BoardState AfterMove(BoardState source, StoneColor[] cells,
        StoneColor nextPlayer, int consecutivePasses, HashSet<string> seen) =>
        new(source.Size, cells, nextPlayer, consecutivePasses, source.MoveNumber + 1, seen);

    public BoardState ResumeAfterScoringDispute() =>
        new(Size, CloneCells(), NextPlayer, 0, MoveNumber, CloneSeen());
}
