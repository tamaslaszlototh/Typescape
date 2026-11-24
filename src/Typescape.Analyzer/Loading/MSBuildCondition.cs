using System.Globalization;

namespace Typescape.Analyzer.Loading;

/// <summary>MSBuild's condition grammar, in three-valued logic: <c>null</c> is "cannot tell".</summary>
internal sealed class MSBuildCondition
{
    private readonly MSBuildExpressions _expressions;
    private readonly List<Token> _tokens;
    private readonly string _directory;
    private int _position;

    private MSBuildCondition(MSBuildExpressions expressions, List<Token> tokens, string directory)
    {
        _expressions = expressions;
        _tokens = tokens;
        _directory = directory;
    }

    /// <param name="directory">What a relative path in <c>Exists()</c> is relative to.</param>
    /// <returns><c>null</c> when it cannot be told, malformed conditions included.</returns>
    public static bool? Evaluate(string condition, MSBuildExpressions expressions, string directory)
    {
        try
        {
            var parser = new MSBuildCondition(expressions, Tokenize(condition), directory);
            var result = parser.Or();
            return parser._position == parser._tokens.Count ? result : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private enum TokenKind
    {
        Text,
        Word,
        Operator,
        Open,
        Close,
        Comma,
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private static List<Token> Tokenize(string condition)
    {
        var tokens = new List<Token>();
        for (var i = 0; i < condition.Length;)
        {
            var c = condition[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c is '\'' or '"' or '`')
            {
                var end = condition.IndexOf(c, i + 1);
                if (end < 0)
                {
                    throw new FormatException();
                }

                tokens.Add(new Token(TokenKind.Text, condition[(i + 1)..end]));
                i = end + 1;
            }
            else if (c == '$' && i + 1 < condition.Length && condition[i + 1] == '(')
            {
                var end = MSBuildExpressions.Closing(condition, i + 1);
                if (end < 0)
                {
                    throw new FormatException();
                }

                tokens.Add(new Token(TokenKind.Text, condition[i..(end + 1)]));
                i = end + 1;
            }
            else if (c is '(' or ')' or ',')
            {
                tokens.Add(new Token(c == '(' ? TokenKind.Open : c == ')' ? TokenKind.Close : TokenKind.Comma, c.ToString()));
                i++;
            }
            else if (c is '=' or '!' or '<' or '>')
            {
                var length = i + 1 < condition.Length && condition[i + 1] == '=' ? 2 : 1;
                var text = condition.Substring(i, length);
                if (text == "=")
                {
                    throw new FormatException();
                }

                tokens.Add(new Token(TokenKind.Operator, text));
                i += length;
            }
            else
            {
                var start = i;
                while (i < condition.Length && (char.IsLetterOrDigit(condition[i]) || condition[i] is '_' or '.' or '-'))
                {
                    i++;
                }

                if (i == start)
                {
                    throw new FormatException();
                }

                tokens.Add(new Token(TokenKind.Word, condition[start..i]));
            }
        }

        return tokens;
    }

    private bool? Or()
    {
        var result = And();
        while (Keyword("or"))
        {
            var right = And();
            result = result == true || right == true ? true : result is null || right is null ? null : false;
        }

        return result;
    }

    private bool? And()
    {
        var result = Not();
        while (Keyword("and"))
        {
            var right = Not();
            result = result == false || right == false ? false : result is null || right is null ? null : true;
        }

        return result;
    }

    private bool? Not()
    {
        if (Peek() is { Kind: TokenKind.Operator, Text: "!" })
        {
            _position++;
            return !Not();
        }

        return Primary();
    }

    private bool? Primary()
    {
        if (Peek() is { Kind: TokenKind.Open })
        {
            _position++;
            var inner = Or();
            Expect(TokenKind.Close);
            return inner;
        }

        if (Peek() is { Kind: TokenKind.Word } word && Peek(1) is { Kind: TokenKind.Open })
        {
            _position += 2;
            var arguments = new List<string?>();
            while (Peek() is not { Kind: TokenKind.Close })
            {
                if (arguments.Count > 0)
                {
                    Expect(TokenKind.Comma);
                }

                arguments.Add(Operand());
            }

            _position++;
            return Function(word.Text, arguments);
        }

        var left = Operand();
        if (Peek() is { Kind: TokenKind.Operator, Text: not "!" } comparison)
        {
            _position++;
            return Compare(left, comparison.Text, Operand());
        }

        return left?.Trim().ToUpperInvariant() switch
        {
            "TRUE" or "ON" or "YES" => true,
            "FALSE" or "OFF" or "NO" => false,
            _ => null,
        };
    }

    private string? Operand()
    {
        var token = Peek() ?? throw new FormatException();
        if (token.Kind is not (TokenKind.Text or TokenKind.Word))
        {
            throw new FormatException();
        }

        _position++;
        return _expressions.Expand(token.Text);
    }

    private bool? Function(string name, List<string?> arguments)
    {
        if (arguments is not [{ } argument])
        {
            return null;
        }

        argument = argument.Trim();
        if (string.Equals(name, "Exists", StringComparison.OrdinalIgnoreCase))
        {
            if (argument.Length == 0)
            {
                return false;
            }

            return MSBuildPaths.HasWildcard(argument) || MSBuildPaths.Full(argument, _directory) is not { } path
                ? null
                : File.Exists(path) || Directory.Exists(path);
        }

        return string.Equals(name, "HasTrailingSlash", StringComparison.OrdinalIgnoreCase)
            ? MSBuildPaths.HasTrailingSlash(argument)
            : null;
    }

    private static bool? Compare(string? left, string op, string? right)
    {
        if (left is null || right is null)
        {
            return null;
        }

        if (op is "==" or "!=")
        {
            var equal = Number(left) is { } a && Number(right) is { } b
                ? a == b
                : string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            return op == "==" ? equal : !equal;
        }

        int order;
        if (Number(left) is { } x && Number(right) is { } y)
        {
            order = x.CompareTo(y);
        }
        else if (Version.TryParse(left.TrimStart('v'), out var v) && Version.TryParse(right.TrimStart('v'), out var w))
        {
            order = v.CompareTo(w);
        }
        else
        {
            return null;
        }

        return op switch
        {
            "<" => order < 0,
            "<=" => order <= 0,
            ">" => order > 0,
            ">=" => order >= 0,
            _ => throw new FormatException(),
        };
    }

    private static double? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    private bool Keyword(string keyword)
    {
        if (Peek() is { Kind: TokenKind.Word } token && string.Equals(token.Text, keyword, StringComparison.OrdinalIgnoreCase))
        {
            _position++;
            return true;
        }

        return false;
    }

    private void Expect(TokenKind kind)
    {
        if (Peek()?.Kind != kind)
        {
            throw new FormatException();
        }

        _position++;
    }

    private Token? Peek(int offset = 0) =>
        _position + offset < _tokens.Count ? _tokens[_position + offset] : null;
}
