using System.Globalization;
using Yijing.Domain.Board;

namespace Yijing.Application.Games;

public enum GameEndReason { Score, Resignation }

public sealed record GameResult(StoneColor Winner, GameEndReason Reason, double? Margin)
{
    public string SgfValue => Reason == GameEndReason.Resignation
        ? $"{(Winner == StoneColor.Black ? "B" : "W")}+R"
        : $"{(Winner == StoneColor.Black ? "B" : "W")}+{Margin?.ToString("0.0", CultureInfo.InvariantCulture)}";
}
