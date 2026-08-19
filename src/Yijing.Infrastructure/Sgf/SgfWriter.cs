using System.Globalization;
using System.Text;
using Yijing.Domain.Board;

namespace Yijing.Infrastructure.Sgf;

/// <summary>Writes an FF[4] SGF containing the game's primary variation.</summary>
public static class SgfWriter
{
    public static string Write(SgfGame game)
    {
        if (game is null)
            throw new ArgumentNullException(nameof(game));

        SgfGame.ValidateBoardSize(game.BoardSize);
        SgfGame.ValidateKomi(game.Komi);

        var output = new StringBuilder();
        output.Append("(;GM[1]FF[4]CA[UTF-8]AP[Yijing:1.0]SZ[")
            .Append(game.BoardSize.ToString(CultureInfo.InvariantCulture))
            .Append("]KM[")
            .Append(FormatKomi(game.Komi))
            .Append("]RU[Chinese]PB[")
            .Append(Escape(game.BlackName))
            .Append("]PW[")
            .Append(Escape(game.WhiteName))
            .Append("]DT[")
            .Append(game.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append(']');

        if (game.Result is not null)
            output.Append("RE[").Append(Escape(game.Result)).Append(']');

        foreach (var move in game.Moves)
            AppendMove(output, move, game.BoardSize);

        return output.Append(')').ToString();
    }

    private static void AppendMove(StringBuilder output, SgfMove move, int boardSize)
    {
        var color = move.Color switch
        {
            StoneColor.Black => 'B',
            StoneColor.White => 'W',
            _ => throw new ArgumentOutOfRangeException(nameof(move), "An SGF move must be Black or White.")
        };

        output.Append(';').Append(color).Append('[');
        if (move.Move.Kind == MoveKind.Play)
        {
            var point = move.Move.Point;
            if (!point.IsInside(boardSize))
                throw new ArgumentOutOfRangeException(nameof(move), "An SGF move coordinate is outside the board.");

            output.Append(IndexToLetter(point.Column)).Append(IndexToLetter(point.Row));
        }
        else if (move.Move.Kind != MoveKind.Pass)
        {
            throw new ArgumentOutOfRangeException(nameof(move), "The SGF move kind is invalid.");
        }
        output.Append(']');
    }

    private static char IndexToLetter(int index) =>
        (char)('a' + index + (index >= 8 ? 1 : 0));

    private static string FormatKomi(double komi) =>
        ExpandScientificNotation(komi.ToString("R", CultureInfo.InvariantCulture));

    private static string ExpandScientificNotation(string value)
    {
        var exponentMarker = value.IndexOfAny('E', 'e');
        if (exponentMarker < 0)
            return value;

        var exponent = int.Parse(value[(exponentMarker + 1)..], NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture);
        var mantissa = value[..exponentMarker];
        var isNegative = mantissa[0] == '-';
        if (isNegative || mantissa[0] == '+')
            mantissa = mantissa[1..];

        var decimalMarker = mantissa.IndexOf('.');
        var digits = decimalMarker < 0 ? mantissa : mantissa.Remove(decimalMarker, 1);
        var decimalIndex = (decimalMarker < 0 ? mantissa.Length : decimalMarker) + exponent;
        var sign = isNegative ? "-" : string.Empty;

        if (decimalIndex <= 0)
            return string.Concat(sign, "0.", new string('0', -decimalIndex), digits);
        if (decimalIndex >= digits.Length)
            return string.Concat(sign, digits, new string('0', decimalIndex - digits.Length));

        return string.Concat(sign, digits.AsSpan(0, decimalIndex), ".", digits.AsSpan(decimalIndex));
    }

    private static string Escape(string value) =>
        (value ?? throw new ArgumentNullException(nameof(value)))
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
}
