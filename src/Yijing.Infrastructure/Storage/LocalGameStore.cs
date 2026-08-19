using Yijing.Application.Persistence;

namespace Yijing.Infrastructure.Storage;

public sealed class LocalGameStore : IGameStore
{
    private const string SnapshotFileName = "active-game.json";
    public const string ApplicationDirectoryName = "Yijing";
    private readonly AtomicJsonStore _store;

    public LocalGameStore(string? localAppData = null)
    {
        var appData = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _store = new AtomicJsonStore(GetAutosaveDirectory(appData));
    }

    public static string GetAutosaveDirectory(string localAppData) =>
        Path.GetFullPath(Path.Combine(localAppData, ApplicationDirectoryName, "autosave"));

    public Task SaveAsync(GameSnapshotDto snapshot, CancellationToken cancellationToken) =>
        _store.WriteAsync(SnapshotFileName, snapshot, cancellationToken);

    public Task<GameSnapshotDto?> LoadAsync(CancellationToken cancellationToken) =>
        _store.ReadAsync<GameSnapshotDto>(SnapshotFileName, cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken) =>
        _store.DeleteAsync(SnapshotFileName, cancellationToken);
}
