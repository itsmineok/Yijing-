using System.Globalization;
using System.Text;
using Yijing.Domain.Board;

namespace Yijing.Infrastructure.Sgf;

/// <summary>Reads the root metadata and primary variation from an FF[4] SGF collection.</summary>
public static class SgfReader
{
    public static SgfGame Read(string text)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text));

        var parser = new Parser(text);
        var root = parser.ParseSingleGameTree();
        return CreateGame(root);
    }

    private static SgfGame CreateGame(GameTree root)
    {
        var rootNode = root.Nodes[0];
        var boardSize = ParseBoardSize(GetSingleValue(rootNode, "SZ") ?? "19");
        var komi = ParseKomi(GetSingleValue(rootNode, "KM") ?? "0");
        var blackName = GetSingleValue(rootNode, "PB") ?? string.Empty;
        var whiteName = GetSingleValue(rootNode, "PW") ?? string.Empty;
        var result = GetSingleValue(rootNode, "RE");
        var date = ParseDate(GetSingleValue(rootNode, "DT"));

        ValidateRequiredRootProperty(rootNode, "GM", "1");
        ValidateRequiredRootProperty(rootNode, "FF", "4");

        var moves = new List<SgfMove>();
        var hasVariations = false;
        for (var tree = root; ; tree = tree.Children[0])
        {
            foreach (var node in tree.Nodes)
                AddMove(node, boardSize, moves);

            if (tree.Children.Count == 0)
                break;

            if (tree.Children.Count > 1)
                hasVariations = true;
        }

        return new SgfGame(boardSize, komi, blackName, whiteName, moves, result, hasVariations, date);
    }

    private static void AddMove(Node node, int boardSize, ICollection<SgfMove> moves)
    {
        var black = GetSingleValue(node, "B");
        var white = GetSingleValue(node, "W");
        if (black is not null && white is not null)
            throw new FormatException("An SGF node cannot contain both Black and White moves.");

        if (black is not null)
            moves.Add(new SgfMove(StoneColor.Black, ParseMove(black, boardSize)));
        else if (white is not null)
            moves.Add(new SgfMove(StoneColor.White, ParseMove(white, boardSize)));
    }

    private static Move ParseMove(string value, int boardSize)
    {
        if (value.Length == 0)
            return Move.Pass();

        if (value.Length != 2 || value[0] is < 'a' or > 'z' || value[1] is < 'a' or > 'z')
            throw new FormatException($"Invalid SGF move coordinate '{value}'; expected two lowercase letters or an empty pass.");

        if (value[0] == 'i' || value[1] == 'i')
            throw new FormatException($"Invalid SGF move coordinate '{value}'; the letter 'i' is not used in SGF coordinates.");

        var column = LetterToIndex(value[0]);
        var row = LetterToIndex(value[1]);
        var point = new BoardPoint(row, column);
        if (!point.IsInside(boardSize))
            throw new FormatException($"SGF move coordinate '{value}' is outside the {boardSize}x{boardSize} board.");

        return Move.Play(point);
    }

    private static int LetterToIndex(char letter) =>
        letter - 'a' - (letter > 'i' ? 1 : 0);

    private static int ParseBoardSize(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var boardSize))
            throw new FormatException($"Invalid SGF SZ value '{value}'; expected an integer from 2 to 19.");

        try
        {
            SgfGame.ValidateBoardSize(boardSize);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new FormatException($"Invalid SGF SZ value '{value}'; expected an integer from 2 to 19.", exception);
        }

        return boardSize;
    }

    private static double ParseKomi(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var komi))
            throw new FormatException($"Invalid SGF KM value '{value}'; expected a finite number.");

        try
        {
            SgfGame.ValidateKomi(komi);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new FormatException(
                $"Invalid SGF KM value '{value}'; expected a finite number.",
                exception);
        }

        return komi;
    }

    private static string? GetSingleValue(Node node, string identifier)
    {
        if (!node.Properties.TryGetValue(identifier, out var values))
            return null;
        if (values.Count != 1)
            throw new FormatException($"SGF property {identifier} must contain exactly one value.");
        return values[0];
    }

    private static DateOnly ParseDate(string? value)
    {
        if (value is null)
            return DateOnly.FromDateTime(DateTime.UtcNow);

        var parser = new DateExpressionParser(value);
        return parser.Parse();
    }

    private sealed class DateExpressionParser
    {
        private readonly string _value;
        private int _index;

        public DateExpressionParser(string value) => _value = value;

        public DateOnly Parse()
        {
            SkipWhitespace();
            var first = ParseFullDate();
            SkipWhitespace();

            if (IsAtEnd)
                return first;

            if (Peek() == '-')
            {
                _index++;
                SkipWhitespace();
                var last = ParseFullDate();
                SkipWhitespace();
                if (!IsAtEnd || last < first)
                    Throw();
                return first;
            }

            if (Peek() != ',')
                Throw();

            var reference = first;
            while (!IsAtEnd)
            {
                Expect(',');
                SkipWhitespace();
                reference = StartsFullDate() ? ParseFullDate() : ParseAbbreviatedDay(reference);
                SkipWhitespace();
                if (IsAtEnd)
                    return first;
                if (Peek() != ',')
                    Throw();
            }

            Throw();
            return default;
        }

        private DateOnly ParseFullDate()
        {
            var remainder = _value.AsSpan(_index);
            var length = remainder.Length >= 10 && remainder[4] == '-' && remainder[7] == '-'
                ? 10
                : 8;
            if (_value.Length - _index < length)
                Throw();

            var text = _value.AsSpan(_index, length);
            var format = length == 10 ? "yyyy-MM-dd" : "yyyyMMdd";
            if (!DateOnly.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                Throw();

            _index += length;
            return date;
        }

        private DateOnly ParseAbbreviatedDay(DateOnly reference)
        {
            if (_value.Length - _index < 2 ||
                !char.IsAsciiDigit(_value[_index]) ||
                !char.IsAsciiDigit(_value[_index + 1]))
                Throw();

            var day = ((_value[_index] - '0') * 10) + (_value[_index + 1] - '0');
            _index += 2;
            try
            {
                return new DateOnly(reference.Year, reference.Month, day);
            }
            catch (ArgumentOutOfRangeException)
            {
                Throw();
                return default;
            }
        }

        private bool StartsFullDate()
        {
            var remainder = _value.AsSpan(_index);
            if (remainder.Length >= 10 && remainder[4] == '-' && remainder[7] == '-')
                return true;
            if (remainder.Length < 8)
                return false;

            for (var index = 0; index < 8; index++)
            {
                if (!char.IsAsciiDigit(remainder[index]))
                    return false;
            }

            return true;
        }

        private void Expect(char expected)
        {
            if (Peek() != expected)
                Throw();
            _index++;
        }

        private void SkipWhitespace()
        {
            while (!IsAtEnd && char.IsWhiteSpace(_value[_index]))
                _index++;
        }

        private char Peek() => IsAtEnd ? '\0' : _value[_index];

        private bool IsAtEnd => _index >= _value.Length;

        private void Throw() => throw new FormatException(
            $"Invalid SGF DT value '{_value}'; expected a valid yyyy-MM-dd or yyyyMMdd date, date list, or date range.");
    }

    private static void ValidateRequiredRootProperty(Node rootNode, string identifier, string expected)
    {
        var value = GetSingleValue(rootNode, identifier);
        if (!string.Equals(value, expected, StringComparison.Ordinal))
            throw new FormatException($"SGF root property {identifier} must be {expected}.");
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _index;

        public Parser(string text) => _text = text;

        public GameTree ParseSingleGameTree()
        {
            SkipWhitespace();
            var tree = ParseGameTree();
            SkipWhitespace();
            if (!IsAtEnd)
                Throw("Unexpected content after the SGF root game tree.");
            return tree;
        }

        private GameTree ParseGameTree()
        {
            Expect('(');
            SkipWhitespace();

            var nodes = new List<Node>();
            while (Peek() == ';')
                nodes.Add(ParseNode());

            if (nodes.Count == 0)
                Throw("An SGF game tree must contain at least one node.");

            var children = new List<GameTree>();
            SkipWhitespace();
            while (Peek() == '(')
            {
                children.Add(ParseGameTree());
                SkipWhitespace();
            }

            Expect(')');
            return new GameTree(nodes, children);
        }

        private Node ParseNode()
        {
            Expect(';');
            SkipWhitespace();
            var properties = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            while (IsUppercaseIdentifierStart(Peek()))
            {
                var identifier = ParseIdentifier();
                SkipWhitespace();
                if (Peek() != '[')
                    Throw($"SGF property {identifier} is missing a value.");

                var values = new List<string>();
                while (Peek() == '[')
                    values.Add(ParseValue());

                if (!properties.TryAdd(identifier, values))
                    Throw($"SGF node contains duplicate property {identifier}.");
                SkipWhitespace();
            }

            return new Node(properties);
        }

        private string ParseIdentifier()
        {
            var start = _index;
            while (IsUppercaseIdentifierStart(Peek()))
                _index++;
            return _text[start.._index];
        }

        private string ParseValue()
        {
            Expect('[');
            var value = new StringBuilder();
            while (true)
            {
                if (IsAtEnd)
                    Throw("An SGF property value is missing its closing bracket.");

                var current = _text[_index++];
                if (current == ']')
                    return value.ToString();

                if (current != '\\')
                {
                    value.Append(current);
                    continue;
                }

                if (IsAtEnd)
                    Throw("An SGF property value ends with an incomplete escape sequence.");

                var escaped = _text[_index++];
                if (escaped == '\r')
                {
                    if (!IsAtEnd && _text[_index] == '\n')
                        _index++;
                    continue;
                }
                if (escaped == '\n')
                    continue;

                value.Append(escaped);
            }
        }

        private void Expect(char expected)
        {
            if (Peek() != expected)
                Throw($"Expected character '{expected}'.");
            _index++;
        }

        private char Peek() => IsAtEnd ? '\0' : _text[_index];

        private bool IsAtEnd => _index >= _text.Length;

        private void SkipWhitespace()
        {
            while (!IsAtEnd && char.IsWhiteSpace(_text[_index]))
                _index++;
        }

        private static bool IsUppercaseIdentifierStart(char value) => value is >= 'A' and <= 'Z';

        private void Throw(string message) =>
            throw new FormatException($"{message} Position {_index}.");
    }

    private sealed record GameTree(IReadOnlyList<Node> Nodes, IReadOnlyList<GameTree> Children);

    private sealed record Node(IReadOnlyDictionary<string, List<string>> Properties);
}
