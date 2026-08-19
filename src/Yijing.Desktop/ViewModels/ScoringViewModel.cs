using System.Globalization;
using System.Windows.Input;
using Yijing.Desktop.Services;
using Yijing.Domain.Board;
using Yijing.Domain.Scoring;

namespace Yijing.Desktop.ViewModels;

public sealed class ScoringViewModel : ObservableObject
{
    private readonly BoardState _state;
    private readonly double _komi;
    private readonly HashSet<BoardPoint> _deadStones = [];
    private IReadOnlySet<BoardPoint> _deadStoneSnapshot;
    private ScoreResult _score;

    public ScoringViewModel(BoardState state, double komi, IReadOnlyList<double>? ownership)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _komi = komi;
        if (!double.IsFinite(komi)) throw new ArgumentOutOfRangeException(nameof(komi));

        InitializeSuggestions(ownership);
        _deadStoneSnapshot = _deadStones.ToHashSet();
        _score = ChineseAreaScorer.Score(_state, _deadStones, _komi);
        ToggleDeadCommand = new RelayCommand(parameter =>
        {
            if (parameter is BoardPoint point) ToggleDeadGroup(point);
        }, parameter => parameter is BoardPoint point && _state.At(point) != StoneColor.Empty);
    }

    public BoardState State => _state;
    public IReadOnlySet<BoardPoint> DeadStones => _deadStoneSnapshot;
    public ScoreResult Score => _score;
    public ICommand ToggleDeadCommand { get; }
    public string ScoreText => string.Create(CultureInfo.CurrentCulture,
        $"黑 {Score.BlackArea} · 白 {Score.WhiteArea}+{Score.Komi:0.0} · {(Score.Winner == StoneColor.Black ? "黑" : "白")}胜 {Score.Margin:0.0} 目");

    public void ToggleDeadGroup(BoardPoint point)
    {
        var color = _state.At(point);
        if (color == StoneColor.Empty) return;

        var group = ConnectedGroup(point, color);
        var markDead = group.Any(candidate => !_deadStones.Contains(candidate));
        foreach (var member in group)
        {
            if (markDead) _deadStones.Add(member);
            else _deadStones.Remove(member);
        }

        _score = ChineseAreaScorer.Score(_state, _deadStones, _komi);
        _deadStoneSnapshot = _deadStones.ToHashSet();
        OnPropertyChanged(nameof(DeadStones));
        OnPropertyChanged(nameof(Score));
        OnPropertyChanged(nameof(ScoreText));
    }

    private void InitializeSuggestions(IReadOnlyList<double>? ownership)
    {
        if (ownership is null || ownership.Count != _state.Size * _state.Size) return;
        for (var row = 0; row < _state.Size; row++)
        for (var column = 0; column < _state.Size; column++)
        {
            var point = new BoardPoint(row, column);
            var stone = _state.At(point);
            if (stone == StoneColor.Empty) continue;
            var confidence = ownership[(row * _state.Size) + column];
            if (!double.IsFinite(confidence) || Math.Abs(confidence) < .95) continue;
            var predictedOwner = confidence > 0 ? StoneColor.Black : StoneColor.White;
            if (predictedOwner != stone) _deadStones.Add(point);
        }
    }

    private HashSet<BoardPoint> ConnectedGroup(BoardPoint start, StoneColor color)
    {
        var group = new HashSet<BoardPoint> { start };
        var queue = new Queue<BoardPoint>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var point))
        {
            foreach (var neighbor in Neighbors(point))
            {
                if (_state.At(neighbor) == color && group.Add(neighbor)) queue.Enqueue(neighbor);
            }
        }
        return group;
    }

    private IEnumerable<BoardPoint> Neighbors(BoardPoint point)
    {
        var candidates = new[]
        {
            new BoardPoint(point.Row - 1, point.Column),
            new BoardPoint(point.Row + 1, point.Column),
            new BoardPoint(point.Row, point.Column - 1),
            new BoardPoint(point.Row, point.Column + 1),
        };
        return candidates.Where(candidate => candidate.IsInside(_state.Size));
    }
}
