using System.Globalization;
using ControlSpace.Core;
namespace ControlSpace.Languages;

/// <summary>A bounded, interpreter-friendly SCL subset parser. No runtime code generation or eval.</summary>
public sealed class SclParser
{
    private sealed record Token(string Text, int Line, int Column, bool Quoted = false, int Start = 0, int End = 0);
    private readonly List<Token> _tokens;
    private readonly IReadOnlyDictionary<string, int> _symbols;
    private readonly IReadOnlyList<PlcTag> _tags;
    private int _position, _depth;
    public SclParser(string source, IReadOnlyDictionary<string, int> symbols, IReadOnlyList<PlcTag> tags)
    {
        if (source.Length > 1_000_000) throw new SclException("Source exceeds 1 MB.", 1, 1);
        _tokens = Lex(source); _symbols = symbols; _tags = tags;
    }
    public IReadOnlyList<Statement> Parse()
    {
        var statements = Statements();
        if (Peek.Text != "<EOF>") Fail("Unexpected token '" + Peek.Text + "'.");
        return statements;
    }
    private Token Peek => _tokens[_position];
    private bool Is(string value) => !Peek.Quoted && Peek.Text.Equals(value, StringComparison.OrdinalIgnoreCase);
    private bool Take(string value) { if (!Is(value)) return false; _position++; return true; }
    private Token Next() => _tokens[_position++];
    private void Need(string value) { if (!Take(value)) Fail("Expected '" + value + "'."); }
    private void Fail(string message) => throw new SclException(message, Peek.Line, Peek.Column);
    private IReadOnlyList<Statement> Statements()
    {
        if (++_depth > 64) Fail("Nesting exceeds 64 levels.");
        var output = new List<Statement>();
        while (!Is("<EOF>") && !Is("ELSE") && !Is("END_IF"))
        {
            if (Take(";")) continue;
            if (Take("IF"))
            {
                var condition = Expr();
                if (condition.Type != PlcType.Bool) Fail("IF requires a BOOL expression.");
                Need("THEN"); var then = Statements();
                var otherwise = Take("ELSE") ? Statements() : Array.Empty<Statement>();
                Need("END_IF"); Need(";"); output.Add(new IfStatement(condition, then, otherwise));
            }
            else
            {
                var token = Next();
                if (!_symbols.TryGetValue(token.Text, out int slot)) throw new SclException("Unknown tag '" + token.Text + "' or unsupported statement.", token.Line, token.Column);
                if (PlcValues.IsInput(_tags[slot].Address)) throw new SclException("Program cannot assign to an input image tag.", token.Line, token.Column);
                Need(":="); var value = Expr(); Need(";");
                if ((_tags[slot].Type == PlcType.Bool) != (value.Type == PlcType.Bool)) throw new SclException("BOOL and numeric assignments cannot be mixed.", token.Line, token.Column);
                output.Add(new AssignmentStatement(slot, value));
            }
        }
        _depth--; return output;
    }
    private static int Precedence(string op) => op.ToUpperInvariant() switch
    {
        "OR" => 1, "XOR" => 2, "AND" => 3,
        "=" or "<>" or ">" or "<" or ">=" or "<=" => 4,
        "+" or "-" => 5, "*" or "/" or "MOD" => 6, _ => 0
    };
    private Expression Expr(int minimum = 1)
    {
        if (++_depth > 64) Fail("Expression nesting exceeds 64 levels.");
        Expression left;
        if (Take("NOT")) { var operand = Expr(7); if (operand.Type != PlcType.Bool) Fail("NOT requires BOOL."); left = new UnaryExpression("NOT", operand, PlcType.Bool); }
        else if (Is("-") || Is("+")) { var op = Next().Text; var operand = Expr(7); if (operand.Type == PlcType.Bool) Fail("Unary arithmetic requires a number."); left = new UnaryExpression(op, operand, PlcType.Real); }
        else if (Take("(")) { left = Expr(); Need(")"); }
        else if (Take("TRUE")) left = new LiteralExpression(1, PlcType.Bool);
        else if (Take("FALSE")) left = new LiteralExpression(0, PlcType.Bool);
        else
        {
            if (Is("<EOF>")) Fail("Expected expression.");
            var token = Next();
            if (!token.Quoted && double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)) left = new LiteralExpression(number, PlcType.Real);
            else if (_symbols.TryGetValue(token.Text, out int slot)) left = new TagExpression(slot, _tags[slot].Type);
            else throw new SclException("Unknown tag or literal '" + token.Text + "'.", token.Line, token.Column);
        }
        while (!Peek.Quoted && Precedence(Peek.Text) >= minimum)
        {
            string op = Next().Text.ToUpperInvariant();
            var right = Expr(Precedence(op) + 1);
            bool boolean = op is "AND" or "OR" or "XOR";
            bool comparison = op is "=" or "<>" or ">" or "<" or ">=" or "<=";
            if (boolean ? left.Type != PlcType.Bool || right.Type != PlcType.Bool : comparison ? (left.Type == PlcType.Bool) != (right.Type == PlcType.Bool) : left.Type == PlcType.Bool || right.Type == PlcType.Bool) Fail("Incompatible expression types.");
            left = new BinaryExpression(op, left, right, boolean || comparison ? PlcType.Bool : PlcType.Real);
        }
        _depth--; return left;
    }
    /// <summary>Rewrites complete symbol tokens without changing comments or longer names.</summary>
    public static string RenameSymbol(string source, string oldName, string newName)
    {
        static bool IsKeyword(string text) => text.ToUpperInvariant() is "IF" or "THEN" or "ELSE" or "END_IF" or "TRUE" or "FALSE" or "AND" or "OR" or "XOR" or "NOT" or "MOD";
        var tokens = Lex(source).Where(t => t.End > t.Start && (t.Quoted || !IsKeyword(t.Text)) && t.Text.Equals(oldName, StringComparison.OrdinalIgnoreCase)).Reverse();
        foreach (var token in tokens)
        {
            string replacement = token.Quoted || IsKeyword(newName) ? "\"" + newName + "\"" : newName;
            source = source[..token.Start] + replacement + source[token.End..];
        }
        return source;
    }
    private static List<Token> Lex(string source)
    {
        var tokens = new List<Token>(); int i = 0, line = 1, column = 1;
        void Advance()
        {
            char current = source[i++];
            if (current == '\r' || current == '\n' && (i < 2 || source[i - 2] != '\r')) { line++; column = 1; }
            else if (current != '\n') column++;
        }
        while (i < source.Length)
        {
            if (tokens.Count >= 100000) throw new SclException("Token limit exceeded.", line, column);
            if (char.IsWhiteSpace(source[i])) { Advance(); continue; }
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/') { while (i < source.Length && source[i] is not ('\r' or '\n')) Advance(); continue; }
            if (source[i] == '(' && i + 1 < source.Length && source[i + 1] == '*')
            {
                Advance(); Advance();
                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == ')')) Advance();
                if (i + 1 >= source.Length) throw new SclException("Unterminated comment.", line, column);
                Advance(); Advance(); continue;
            }
            int start = i, sl = line, sc = column;
            if (source[i] == '"')
            {
                Advance(); int nameStart = i;
                while (i < source.Length && source[i] != '"') Advance();
                if (i >= source.Length) throw new SclException("Unterminated quoted tag name.", sl, sc);
                string text = source[nameStart..i]; Advance(); tokens.Add(new(text, sl, sc, true, start, i)); continue;
            }
            if (char.IsLetter(source[i]) || source[i] == '_')
            {
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) Advance();
            }
            else if (char.IsDigit(source[i]) || source[i] == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1]))
            {
                while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.')) Advance();
                if (i < source.Length && (source[i] == 'e' || source[i] == 'E')) { Advance(); if (i < source.Length && (source[i] == '+' || source[i] == '-')) Advance(); while (i < source.Length && char.IsDigit(source[i])) Advance(); }
            }
            else
            {
                Advance();
                if (i < source.Length && (source[start..(i + 1)] is ":=" or "<=" or ">=" or "<>")) Advance();
                else if (!"()+-*/=<>;".Contains(source[start])) throw new SclException("Unsupported character '" + source[start] + "'.", sl, sc);
            }
            tokens.Add(new(source[start..i], sl, sc, false, start, i));
        }
        tokens.Add(new("<EOF>", line, column)); return tokens;
    }
}
public sealed class SclException(string message, int line, int column) : Exception(message)
{
    public int Line { get; } = line;
    public int Column { get; } = column;
}
