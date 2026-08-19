using Yijing.Domain.Board;

namespace Yijing.Application.Games;

public sealed record GameOptions(GameMode Mode, int BoardSize, StoneColor? HumanColor, double Komi);
