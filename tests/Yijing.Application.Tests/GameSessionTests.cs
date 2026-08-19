using System.Globalization;
using Yijing.Application.Games;
using Yijing.Domain.Board;

namespace Yijing.Application.Tests;

public sealed class GameSessionTests
{
    [Fact]
    public void Undo_AfterAiReplyReturnsToBeforeHumansLastMove()
    {
        var game = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.Black, 7.5));
        game.Play(Move.Play(new BoardPoint(2, 2)));
        game.Play(Move.Play(new BoardPoint(6, 6)));

        Assert.True(game.Undo());
        Assert.Empty(game.Moves);
        Assert.Equal(StoneColor.Black, game.State.NextPlayer);
    }

    [Fact]
    public void Undo_WhileAiIsThinkingRemovesOnlyPendingHumanMove()
    {
        var game = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.Black, 7.5));
        game.Play(Move.Play(new BoardPoint(2, 2)));
        game.SetAiThinking(true);

        Assert.True(game.Undo());
        Assert.Empty(game.Moves);
        Assert.Equal(StoneColor.Black, game.State.NextPlayer);
    }

    [Fact]
    public void Undo_DoesNotRemoveOpeningAiMoveBeforeWhiteHasPlayed()
    {
        var game = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 9, StoneColor.White, 7.5));
        game.Play(Move.Play(new BoardPoint(2, 2)));

        Assert.False(game.Undo());
        Assert.Single(game.Moves);
    }

    [Fact]
    public void Undo_LocalTwoPlayerRemovesExactlyOneMove()
    {
        var game = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));
        game.Play(Move.Play(new BoardPoint(2, 2)));
        game.Play(Move.Play(new BoardPoint(6, 6)));

        Assert.True(game.Undo());
        Assert.Single(game.Moves);
        Assert.Equal(StoneColor.White, game.State.NextPlayer);
    }

    [Fact]
    public void Resign_StoresSgfResultForOpponent()
    {
        var game = GameSession.Start(new GameOptions(GameMode.HumanVsAi, 19, StoneColor.Black, 7.5));

        game.Resign(StoneColor.Black);

        Assert.Equal("W+R", game.Result!.SgfValue);
        Assert.Equal(GameEndReason.Resignation, game.Result.Reason);
    }

    [Fact]
    public void TwoPasses_LeaveResultUnsetAndExposeScoringState()
    {
        var game = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));

        game.Play(Move.Pass());
        game.Play(Move.Pass());

        Assert.Null(game.Result);
        Assert.True(game.State.HasTwoConsecutivePasses);
    }

    [Fact]
    public void RestoreAfterFinish_ClearsResultWithoutChangingMoves()
    {
        var game = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));
        game.Play(Move.Play(new BoardPoint(2, 2)));
        game.Resign(StoneColor.White);
        var revision = game.Revision;

        game.RestoreAfterFinish();

        Assert.Null(game.Result);
        Assert.Single(game.Moves);
        Assert.Equal(revision + 1, game.Revision);
    }

    [Fact]
    public void ResumeAfterScoringDispute_ClearsPassesWithoutChangingMoves()
    {
        var game = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));
        game.Play(Move.Pass());
        game.Play(Move.Pass());
        var revision = game.Revision;

        game.ResumeAfterScoringDispute();

        Assert.False(game.State.HasTwoConsecutivePasses);
        Assert.Equal(0, game.State.ConsecutivePasses);
        Assert.Equal(2, game.Moves.Count);
        Assert.Equal(revision + 1, game.Revision);
    }
    [Fact]
    public void Play_AfterResignationRejectsMoveWithoutChangingStateOrHistory()
    {
        var game = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));
        var state = game.State;

        game.Resign(StoneColor.Black);
        var outcome = game.Play(Move.Play(new BoardPoint(2, 2)));

        Assert.False(outcome.IsLegal);
        Assert.Same(state, game.State);
        Assert.Empty(game.Moves);
        Assert.Equal("W+R", game.Result!.SgfValue);
    }

    [Fact]
    public void Resign_AfterGameFinishedPreservesExistingResult()
    {
        var game = GameSession.Start(new GameOptions(GameMode.LocalTwoPlayer, 9, null, 7.5));
        game.Resign(StoneColor.Black);
        var result = game.Result;

        game.Resign(StoneColor.White);

        Assert.Same(result, game.Result);
        Assert.Equal("W+R", game.Result!.SgfValue);
    }

    [Fact]
    public void SgfValue_UsesInvariantDecimalSeparatorForScoreMargin()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var result = new GameResult(StoneColor.Black, GameEndReason.Score, 1.5);

            Assert.Equal("B+1.5", result.SgfValue);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
