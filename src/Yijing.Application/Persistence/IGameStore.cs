using Yijing.Application.Games;

namespace Yijing.Application.Persistence;

public sealed record GameSnapshotDto(
    GameOptions Options,
    IReadOnlyList<PlayedMove> Moves,
    GameResult? Result,
    long Revision);

public interface IGameStore
{
    Task SaveAsync(GameSnapshotDto snapshot, CancellationToken cancellationToken);
    Task<GameSnapshotDto?> LoadAsync(CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
