using Yijing.Infrastructure.Sgf;

namespace Yijing.Infrastructure.Tests;

public sealed class SgfReaderWriterTests
{
    [Fact]
    public void Read_ParsesMetadataMovesPassAndResult()
    {
        const string text = "(;GM[1]FF[4]CA[UTF-8]AP[Yijing:1.0]SZ[9]KM[7.5]RU[Chinese]PB[玩家]PW[KataGo]RE[W+R];B[cc];W[];B[gg])";

        var game = SgfReader.Read(text);

        Assert.Equal(9, game.BoardSize);
        Assert.Equal(7.5, game.Komi);
        Assert.Equal("W+R", game.Result);
        Assert.Equal(3, game.Moves.Count);
        Assert.True(game.Moves[1].Move.Kind == Yijing.Domain.Board.MoveKind.Pass);
    }

    [Fact]
    public void Read_FollowsFirstVariationAndReportsBranches()
    {
        const string text = "(;GM[1]FF[4]SZ[9]KM[7.5];B[cc](;W[dd])(;W[ee]))";

        var game = SgfReader.Read(text);

        Assert.True(game.HasVariations);
        Assert.Equal(new Yijing.Domain.Board.BoardPoint(3, 3), game.Moves[1].Move.Point);
    }

    [Fact]
    public void WriteThenRead_PreservesMainLineAndEscapedNames()
    {
        var original = SgfGame.Create(9, 7.5, "张]三", "KataGo",
            [
                SgfMove.PlayBlack(2, 2),
                SgfMove.PassWhite()
            ], "B+1.5");

        var reparsed = SgfReader.Read(SgfWriter.Write(original));

        Assert.Equal(original.BlackName, reparsed.BlackName);
        Assert.Equal(original.Moves, reparsed.Moves);
        Assert.Equal("B+1.5", reparsed.Result);
    }

    [Theory]
    [InlineData("2026-08-17")]
    [InlineData("20260817")]
    [InlineData("20260817,20260819")]
    [InlineData("20260817-20260819")]
    [InlineData("20260817,20")]
    public void Read_NormalizesValidDateExpressions(string date)
    {
        var game = SgfReader.Read($"(;GM[1]FF[4]SZ[9]DT[{date}])");

        Assert.Equal(new DateOnly(2026, 8, 17), game.Date);
    }

    [Theory]
    [InlineData("20260817,invalid")]
    [InlineData("20260817-")]
    [InlineData("20261301")]
    [InlineData("20260817,99")]
    public void Read_RejectsInvalidDateExpressions(string date)
    {
        Assert.Throws<FormatException>(() => SgfReader.Read($"(;GM[1]FF[4]SZ[9]DT[{date}])"));
    }

    [Theory]
    [InlineData("7.5")]
    [InlineData("0")]
    [InlineData("6.25")]
    [InlineData("1e-30")]
    [InlineData("1.7976931348623157e+308")]
    public void ReadAndWrite_RoundTripsAnyFiniteKomiWithoutScientificNotation(string input)
    {
        var game = SgfReader.Read($"(;GM[1]FF[4]SZ[9]KM[{input}])");

        var output = SgfWriter.Write(game);
        var reparsed = SgfReader.Read(output);
        var komiText = output.Split("KM[", StringSplitOptions.None)[1].Split(']')[0];

        Assert.DoesNotContain("E", komiText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(BitConverter.DoubleToInt64Bits(game.Komi), BitConverter.DoubleToInt64Bits(reparsed.Komi));
    }

    [Fact]
    public void Create_AllowsFiniteKomiBelowTheSmallestHalfPointIncrement()
    {
        var game = SgfGame.Create(9, 1e-30, "Black", "White", []);

        Assert.Equal(1e-30, game.Komi);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void Read_RejectsNonFiniteKomi(string value)
    {
        Assert.Throws<FormatException>(() => SgfReader.Read($"(;GM[1]FF[4]SZ[9]KM[{value}])"));
    }

    [Fact]
    public void Create_RejectsNonFiniteKomi()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SgfGame.Create(9, double.NaN, "Black", "White", []));
    }

    [Fact]
    public void Read_RejectsMalformedSyntaxWithEnglishPosition()
    {
        var error = Assert.Throws<FormatException>(() => SgfReader.Read("(;GM[1]FF[4]SZ[9]"));

        Assert.Contains("position", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_RejectsCoordinatesOutsideTheBoardWithEnglishMessage()
    {
        var error = Assert.Throws<FormatException>(() => SgfReader.Read("(;GM[1]FF[4]SZ[9];B[zz])"));

        Assert.Contains("outside", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("cf", 5, 2)]
    [InlineData("jc", 2, 8)]
    [InlineData("tt", 18, 18)]
    [InlineData("aa", 0, 0)]
    public void Read_InterpretsFirstLetterAsColumnAndSkipsLetterI(
        string coordinate, int expectedRow, int expectedColumn)
    {
        var game = SgfReader.Read($"(;GM[1]FF[4]SZ[19];B[{coordinate}])");

        Assert.Equal(new Yijing.Domain.Board.BoardPoint(expectedRow, expectedColumn),
            game.Moves[0].Move.Point);
    }

    [Fact]
    public void Read_LoadsRightEdgeMoveWithoutError()
    {
        var game = SgfReader.Read("(;GM[1]FF[4]SZ[19];B[tc])");

        Assert.Equal(new Yijing.Domain.Board.BoardPoint(2, 18), game.Moves[0].Move.Point);
    }

    [Fact]
    public void Read_RejectsLetterIInCoordinates()
    {
        Assert.Throws<FormatException>(() => SgfReader.Read("(;GM[1]FF[4]SZ[19];B[ic])"));
    }

    [Fact]
    public void Write_WritesColumnBeforeRow()
    {
        var game = SgfGame.Create(19, 7.5, "Black", "White",
            [SgfMove.PlayBlack(5, 2)], null);

        var text = SgfWriter.Write(game);

        Assert.Contains(";B[cf]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_SkipsLetterIAndReachesRightBottomCorner()
    {
        var game = SgfGame.Create(19, 7.5, "Black", "White",
            [SgfMove.PlayBlack(8, 18), SgfMove.PlayWhite(18, 18)], null);

        var text = SgfWriter.Write(game);

        Assert.Contains(";B[tj]", text, StringComparison.Ordinal);
        Assert.Contains(";W[tt]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_UnescapesBackslashBracketAndEscapedLineEnding()
    {
        const string text = """
            (;GM[1]FF[4]SZ[9]PB[A\\\]B\
            C])
            """;

        var game = SgfReader.Read(text);

        Assert.Equal("A\\]BC", game.BlackName);
        Assert.Equal(game.BlackName, SgfReader.Read(SgfWriter.Write(game)).BlackName);
    }
}
