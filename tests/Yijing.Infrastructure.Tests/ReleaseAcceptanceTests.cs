using Yijing.Domain.Board;
using Yijing.Domain.Rules;
using Yijing.Infrastructure.Sgf;

namespace Yijing.Infrastructure.Tests;

public sealed class ReleaseAcceptanceTests
{
    [Fact]
    public void ScriptedNineByNineGameRoundTripsWithSameFinalBoardKey()
    {
        SgfMove[] moves =
        [
            SgfMove.PlayBlack(2, 2),
            SgfMove.PlayWhite(6, 6),
            SgfMove.PlayBlack(2, 6),
            SgfMove.PlayWhite(6, 2),
            SgfMove.PlayBlack(4, 4),
            SgfMove.PassWhite()
        ];

        var before = Replay(9, moves);
        var serialized = SgfWriter.Write(SgfGame.Create(9, 7.5, "Human", "KataGo", moves));
        var loaded = SgfReader.Read(serialized);
        var after = Replay(loaded.BoardSize, loaded.Moves);

        Assert.Equal(Key(before), Key(after));
    }

    private static BoardState Replay(int size, IEnumerable<SgfMove> moves)
    {
        var state = BoardState.Empty(size);
        foreach (var move in moves)
        {
            Assert.Equal(state.NextPlayer, move.Color);
            var result = GoRules.TryApply(state, move.Move);
            Assert.True(result.IsLegal);
            state = result.State!;
        }
        return state;
    }

    private static string Key(BoardState board) => string.Create(
        board.Size * board.Size,
        board,
        static (span, state) =>
        {
            var index = 0;
            for (var row = 0; row < state.Size; row++)
            for (var column = 0; column < state.Size; column++)
                span[index++] = state.At(new BoardPoint(row, column)) switch
                {
                    StoneColor.Black => 'B',
                    StoneColor.White => 'W',
                    _ => '.'
                };
        });
}
