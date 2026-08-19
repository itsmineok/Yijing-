using Yijing.Application.Games;
using Yijing.Application.Persistence;
using Yijing.Domain.Board;
using System.IO;
using Yijing.Desktop.Services;

namespace Yijing.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private GameViewModel _currentGame;
    private readonly Func<GameOptions, GameViewModel>? _gameFactory;
    private readonly IAsyncDisposable? _ownedEngine;

    public MainWindowViewModel(
        GameViewModel currentGame,
        Func<GameOptions, GameViewModel>? gameFactory = null,
        IAsyncDisposable? ownedEngine = null)
    {
        _currentGame = currentGame ?? throw new ArgumentNullException(nameof(currentGame));
        _gameFactory = gameFactory;
        _ownedEngine = ownedEngine;
    }

    public GameViewModel CurrentGame
    {
        get => _currentGame;
        private set => SetProperty(ref _currentGame, value);
    }

    public async Task ReplaceGameAsync(GameViewModel replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var previous = CurrentGame;
        CurrentGame = replacement;
        await previous.DisposeAsync();
    }

    public Task StartNewGameAsync(GameOptions options)
    {
        if (_gameFactory is null) throw new InvalidOperationException("A game factory was not configured.");
        return StartNewGameCoreAsync(options);
    }

    private async Task StartNewGameCoreAsync(GameOptions options)
    {
        await CurrentGame.DisposeAsync();
        CurrentGame = _gameFactory!(options);
    }

    public static GameSession ReplaySnapshot(GameSnapshotDto snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var session = GameSession.Start(snapshot.Options);
        foreach (var playedMove in snapshot.Moves)
        {
            if (playedMove.Color != session.State.NextPlayer)
                throw new InvalidDataException("Autosave move color does not match the replayed position.");
            var moveResult = session.Play(playedMove.Move);
            if (!moveResult.IsLegal)
                throw new InvalidDataException($"Autosave contains an illegal move: {moveResult.IllegalReason}.");
        }

        if (snapshot.Result is { } result)
        {
            if (result.Reason == GameEndReason.Resignation)
                session.Resign(result.Winner.Opponent());
            else if (result.Margin is { } margin)
                session.FinishByScore(result.Winner, margin);
        }
        return session;
    }

    public static async Task<GameSession> RestoreOrStartAsync(
        IGameStore gameStore,
        IDialogService dialogs,
        GameOptions fallbackOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gameStore);
        ArgumentNullException.ThrowIfNull(dialogs);
        var snapshot = await gameStore.LoadAsync(cancellationToken);
        if (snapshot is not null && snapshot.Result is null &&
            await dialogs.ConfirmAsync("恢复对局", "恢复上次未完成的对局吗？"))
        {
            return ReplaySnapshot(snapshot);
        }
        if (snapshot is not null) await gameStore.ClearAsync(cancellationToken);
        return GameSession.Start(fallbackOptions);
    }

    public async ValueTask DisposeAsync()
    {
        await CurrentGame.DisposeAsync();
        if (_ownedEngine is not null) await _ownedEngine.DisposeAsync();
    }
}
