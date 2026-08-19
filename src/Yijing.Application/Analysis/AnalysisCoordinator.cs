using Yijing.Application.Games;
using Yijing.Domain.Board;
using Yijing.Domain.Rules;

namespace Yijing.Application.Analysis;

public sealed class AnalysisCoordinator
{
    private static long _requestSequence;
    private readonly IPositionAnalyzer _analyzer;
    private readonly Func<int, TimeSpan> _durationForMove;
    private readonly IProgress<AnalysisResult> _progress;

    public AnalysisCoordinator(IPositionAnalyzer analyzer, IProgress<AnalysisResult> progress)
        : this(analyzer, TimeSpan.FromSeconds(30), progress)
    {
    }

    public AnalysisCoordinator(
        IPositionAnalyzer analyzer,
        TimeSpan searchDuration,
        IProgress<AnalysisResult> progress)
        : this(analyzer, _ => searchDuration, progress)
    {
        if (searchDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(searchDuration));
    }

    public AnalysisCoordinator(
        IPositionAnalyzer analyzer,
        Func<int, TimeSpan> durationForMove,
        IProgress<AnalysisResult> progress)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(durationForMove);
        ArgumentNullException.ThrowIfNull(progress);

        _analyzer = analyzer;
        _durationForMove = durationForMove;
        _progress = progress;
    }

    public async Task<Move?> FindAiMoveAsync(GameSession session, CancellationToken cancellationToken)
        => await FindAiMoveAsync(session, _progress, cancellationToken).ConfigureAwait(false);

    public async Task<Move?> FindAiMoveAsync(
        GameSession session,
        IProgress<AnalysisResult> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(progress);
        cancellationToken.ThrowIfCancellationRequested();

        var revision = session.Revision;
        var state = session.State;
        var position = new AnalysisPosition(
            session.Options.BoardSize,
            state.NextPlayer,
            session.Moves.ToArray(),
            session.Options.Komi,
            revision);
        var requestId = $"game-{revision}-{Interlocked.Increment(ref _requestSequence)}";
        var revisionChanged = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var externallyCanceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler revisionHandler = (_, _) => revisionChanged.TrySetResult(true);
        session.RevisionChanged += revisionHandler;
        if (session.Revision != revision) revisionChanged.TrySetResult(true);
        using var cancellationRegistration = cancellationToken.Register(
            () => externallyCanceled.TrySetResult(true));

        try
        {
            var analysisTask = ConsumeAnalysisAsync(session, revision, position, requestId, progress);
            var searchDuration = _durationForMove(session.Moves.Count);
            if (searchDuration <= TimeSpan.Zero)
                throw new InvalidOperationException("The move duration function returned a non-positive duration.");
            var deadlineTask = Task.Delay(searchDuration);
            var completed = await Task.WhenAny(
                    analysisTask,
                    deadlineTask,
                    revisionChanged.Task,
                    externallyCanceled.Task)
                .ConfigureAwait(false);

            if (completed == analysisTask)
            {
                var earlyFinal = await analysisTask.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return SelectLegalMove(session, revision, state, earlyFinal);
            }

            await _analyzer.TerminateAsync(requestId, CancellationToken.None).ConfigureAwait(false);
            var final = await analysisTask.ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            if (revisionChanged.Task.IsCompleted || session.Revision != revision) return null;
            return SelectLegalMove(session, revision, state, final);
        }
        finally
        {
            session.RevisionChanged -= revisionHandler;
        }
    }

    private async Task<AnalysisResult?> ConsumeAnalysisAsync(
        GameSession session,
        int revision,
        AnalysisPosition position,
        string requestId,
        IProgress<AnalysisResult> progress)
    {
        AnalysisResult? final = null;
        await foreach (var result in _analyzer.AnalyzeAsync(position, requestId, CancellationToken.None)
                           .ConfigureAwait(false))
        {
            if (result.IsFinal)
            {
                final = result;
                if (session.Revision == revision) progress.Report(result);
                break;
            }
            else if (session.Revision == revision)
            {
                progress.Report(result);
            }
        }

        return final;
    }

    private static Move? SelectLegalMove(
        GameSession session,
        int revision,
        BoardState state,
        AnalysisResult? final)
    {
        if (session.Revision != revision || final is null) return null;

        foreach (var candidate in final.Candidates)
        {
            if (!TryParseMove(candidate.Move, state.Size, out var move)) continue;
            if (GoRules.TryApply(state, move).IsLegal) return move;
        }

        return null;
    }

    private static bool TryParseMove(string value, int boardSize, out Move move)
    {
        move = default;
        if (string.Equals(value, "pass", StringComparison.OrdinalIgnoreCase))
        {
            move = Move.Pass();
            return true;
        }

        if (value.Length < 2) return false;
        var letter = char.ToUpperInvariant(value[0]);
        if (letter is < 'A' or > 'T' || letter == 'I') return false;
        var column = letter - 'A';
        if (letter > 'I') column--;
        if (!int.TryParse(value.AsSpan(1), out var gtpRow)) return false;
        var row = boardSize - gtpRow;
        var point = new BoardPoint(row, column);
        if (!point.IsInside(boardSize)) return false;

        move = Move.Play(point);
        return true;
    }
}
