using System.Collections.ObjectModel;
using Yijing.Domain.Board;

namespace Yijing.Infrastructure.Sgf;

/// <summary>Represents the root metadata and primary variation of an SGF game.</summary>
public sealed record SgfGame
{
    private readonly ReadOnlyCollection<SgfMove> _moves;

    public SgfGame(
        int boardSize,
        double komi,
        string blackName,
        string whiteName,
        IEnumerable<SgfMove> moves,
        string? result,
        bool hasVariations = false)
        : this(boardSize, komi, blackName, whiteName, moves, result, hasVariations,
            DateOnly.FromDateTime(DateTime.UtcNow))
    {
    }

    public SgfGame(
        int boardSize,
        double komi,
        string blackName,
        string whiteName,
        IEnumerable<SgfMove> moves,
        string? result,
        bool hasVariations,
        DateOnly date)
    {
        ValidateBoardSize(boardSize);
        ValidateKomi(komi);

        BoardSize = boardSize;
        Komi = komi;
        BlackName = blackName ?? throw new ArgumentNullException(nameof(blackName));
        WhiteName = whiteName ?? throw new ArgumentNullException(nameof(whiteName));
        _moves = Array.AsReadOnly((moves ?? throw new ArgumentNullException(nameof(moves))).ToArray());
        Result = result;
        HasVariations = hasVariations;
        Date = date;
    }

    public int BoardSize { get; }
    public double Komi { get; }
    public string BlackName { get; }
    public string WhiteName { get; }
    public IReadOnlyList<SgfMove> Moves => _moves;
    public string? Result { get; }
    public bool HasVariations { get; }
    public DateOnly Date { get; }

    public static SgfGame Create(
        int boardSize,
        double komi,
        string blackName,
        string whiteName,
        IEnumerable<SgfMove> moves,
        string? result = null) =>
        new(boardSize, komi, blackName, whiteName, moves, result);

    internal static void ValidateBoardSize(int boardSize)
    {
        if (boardSize is < 2 or > 19)
            throw new ArgumentOutOfRangeException(nameof(boardSize), "SGF board size must be between 2 and 19.");
    }

    internal static void ValidateKomi(double komi)
    {
        if (!double.IsFinite(komi))
            throw new ArgumentOutOfRangeException(nameof(komi), "Komi must be a finite number.");
    }
}

/// <summary>A colored move in an SGF main line.</summary>
public sealed record SgfMove(StoneColor Color, Move Move)
{
    public static SgfMove PlayBlack(int row, int column) =>
        new(StoneColor.Black, Move.Play(new BoardPoint(row, column)));

    public static SgfMove PlayWhite(int row, int column) =>
        new(StoneColor.White, Move.Play(new BoardPoint(row, column)));

    public static SgfMove PassBlack() => new(StoneColor.Black, Move.Pass());

    public static SgfMove PassWhite() => new(StoneColor.White, Move.Pass());
}
