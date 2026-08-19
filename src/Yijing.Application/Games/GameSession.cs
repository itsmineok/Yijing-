using Yijing.Domain.Board;
using Yijing.Domain.Rules;

namespace Yijing.Application.Games;

public sealed class GameSession
{
    private readonly List<BoardState> _states;
    private readonly List<PlayedMove> _moves = [];

    private GameSession(GameOptions options)
    {
        Options = options;
        _states = [BoardState.Empty(options.BoardSize)];
    }

    public GameOptions Options { get; }
    public BoardState State => _states[^1];
    public IReadOnlyList<PlayedMove> Moves => _moves;
    public GameResult? Result { get; private set; }
    public bool IsAiThinking { get; private set; }
    public int Revision { get; private set; }
    public event EventHandler? RevisionChanged;

    public static GameSession Start(GameOptions options) => new(options);

    public MoveResult Play(Move move)
    {
        if (Result is not null) return MoveResult.Illegal(State, IllegalMoveReason.GameFinished);

        var outcome = GoRules.TryApply(State, move);
        if (!outcome.IsLegal) return outcome;

        _moves.Add(new PlayedMove(State.NextPlayer, move));
        _states.Add(outcome.State!);
        IsAiThinking = false;
        BumpRevision();
        return outcome;
    }

    public void SetAiThinking(bool isAiThinking) => IsAiThinking = isAiThinking;

    public bool Undo()
    {
        if (Result is not null || _moves.Count == 0) return false;

        var removeFrom = Options.Mode == GameMode.LocalTwoPlayer
            ? _moves.Count - 1
            : _moves.FindLastIndex(item => item.Color == Options.HumanColor);

        if (removeFrom < 0) return false;
        _moves.RemoveRange(removeFrom, _moves.Count - removeFrom);
        _states.RemoveRange(removeFrom + 1, _states.Count - (removeFrom + 1));
        IsAiThinking = false;
        BumpRevision();
        return true;
    }

    public void Resign(StoneColor color)
    {
        if (Result is not null) return;

        Result = new GameResult(color.Opponent(), GameEndReason.Resignation, null);
        IsAiThinking = false;
        BumpRevision();
    }

    public void FinishByScore(StoneColor winner, double margin)
    {
        if (Result is not null) return;
        if (winner == StoneColor.Empty) throw new ArgumentOutOfRangeException(nameof(winner));
        if (!double.IsFinite(margin) || margin < 0) throw new ArgumentOutOfRangeException(nameof(margin));

        Result = new GameResult(winner, GameEndReason.Score, margin);
        IsAiThinking = false;
        BumpRevision();
    }

    public void RestoreAfterFinish()
    {
        Result = null;
        BumpRevision();
    }

    public void ResumeAfterScoringDispute()
    {
        _states[^1] = State.ResumeAfterScoringDispute();
        BumpRevision();
    }

    private void BumpRevision()
    {
        Revision++;
        RevisionChanged?.Invoke(this, EventArgs.Empty);
    }
}
