using Yijing.Domain.Board;

namespace Yijing.Domain.Scoring;

public sealed record ScoreResult(int BlackArea, int WhiteArea, double Komi,
    StoneColor Winner, double Margin)
{
    public double WhiteTotal => WhiteArea + Komi;
}
