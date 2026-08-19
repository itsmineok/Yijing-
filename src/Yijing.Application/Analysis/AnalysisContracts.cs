using Yijing.Application.Games;
using Yijing.Domain.Board;

namespace Yijing.Application.Analysis;

public sealed record CandidateMove(string Move, double Winrate, double ScoreLead, int Visits);

public sealed record AnalysisResult(
    string RequestId,
    bool IsFinal,
    IReadOnlyList<CandidateMove> Candidates,
    double RootWinrate,
    double RootScoreLead,
    IReadOnlyList<double>? Ownership = null);

public sealed record AnalysisPosition(
    int BoardSize,
    StoneColor NextPlayer,
    IReadOnlyList<PlayedMove> Moves,
    double Komi,
    long GameRevision);

public interface IPositionAnalyzer
{
    IAsyncEnumerable<AnalysisResult> AnalyzeAsync(
        AnalysisPosition position,
        string requestId,
        CancellationToken cancellationToken);

    Task TerminateAsync(string requestId, CancellationToken cancellationToken);
}
