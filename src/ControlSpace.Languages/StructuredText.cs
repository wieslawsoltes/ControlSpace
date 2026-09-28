using System.Globalization;
using ControlSpace.Core;

namespace ControlSpace.Languages;

public enum TextOp { Constant, Load, Store, Add, Subtract, Multiply, Divide, IntegerDivide, Modulo, Negate, Not, And, Or, Xor, Equal, NotEqual, Greater, Less, GreaterOrEqual, LessOrEqual, Abs, Min, Max, Sqrt, Jump, JumpFalse }
public readonly record struct TextInstruction(TextOp Op, double Number = 0, int Argument = 0);
public sealed record SourceReference(string Name, string Access, int Line, int Column);

public sealed class TextProgram(TextInstruction[] instructions, SourceReference[] references)
{
    public IReadOnlyList<TextInstruction> Instructions => instructions;
    public IReadOnlyList<SourceReference> References => references;

    /// <summary>Runs bounded bytecode. The caller owns the transaction and commits memory only on success.</summary>
    public int Execute(double[] values, PlcType[] types, int budget = 100000)
    {
        Span<double> stack = stackalloc double[512];
        var top = 0; var pc = 0; var steps = 0;
        while (pc < instructions.Length)
        {
            if (++steps > budget) throw new InvalidOperationException("Structured-text instruction budget exceeded; scan rolled back.");
            var op = instructions[pc++];
            switch (op.Op)
            {
                case TextOp.Constant: stack[top++] = op.Number; break;
                case TextOp.Load: stack[top++] = values[op.Argument]; break;
                case TextOp.Store: values[op.Argument] = PlcValues.Normalize(types[op.Argument], stack[--top]); break;
                case TextOp.Jump: pc = op.Argument; break;
                case TextOp.JumpFalse: if (stack[--top] == 0) pc = op.Argument; break;
                case TextOp.Negate: stack[top - 1] = -stack[top - 1]; break;
                case TextOp.Not: stack[top - 1] = stack[top - 1] == 0 ? 1 : 0; break;
                case TextOp.Abs: stack[top - 1] = Math.Abs(stack[top - 1]); break;
                case TextOp.Sqrt:
                    if (stack[top - 1] < 0) throw new ArithmeticException("SQRT argument is negative.");
                    stack[top - 1] = Math.Sqrt(stack[top - 1]); break;
                default:
                    var right = stack[--top]; var left = stack[top - 1];
                    stack[top - 1] = op.Op switch
                    {
                        TextOp.Add => left + right,
                        TextOp.Subtract => left - right,
                        TextOp.Multiply => left * right,
                        TextOp.Divide => right == 0 ? throw new DivideByZeroException() : left / right,
                        TextOp.IntegerDivide => right == 0 ? throw new DivideByZeroException() : Math.Truncate(left / right),
                        TextOp.Modulo => right == 0 ? throw new DivideByZeroException() : left % right,
                        TextOp.And => left != 0 && right != 0 ? 1 : 0,
                        TextOp.Or => left != 0 || right != 0 ? 1 : 0,
                        TextOp.Xor => (left != 0) != (right != 0) ? 1 : 0,
                        TextOp.Equal => left == right ? 1 : 0,
                        TextOp.NotEqual => left != right ? 1 : 0,
                        TextOp.Greater => left > right ? 1 : 0,
                        TextOp.Less => left < right ? 1 : 0,
                        TextOp.GreaterOrEqual => left >= right ? 1 : 0,
                        TextOp.LessOrEqual => left <= right ? 1 : 0,
                        TextOp.Min => Math.Min(left, right),
                        TextOp.Max => Math.Max(left, right),
                        _ => throw new InvalidOperationException("Invalid bytecode instruction.")
                    };
                    break;
            }
            if (top is < 0 or >= 512) throw new InvalidOperationException("Expression stack limit exceeded.");
            if (top > 0 && !double.IsFinite(stack[top - 1])) throw new ArithmeticException("Non-finite arithmetic result.");
        }
        if (top != 0) throw new InvalidOperationException("Unbalanced expression stack.");
        return steps;
    }
}

public sealed class TextCompileException(string message, int line, int column) : Exception(message)
{
    public int Line { get; } = line;
    public int Column { get; } = column;
}

public static class StructuredTextCompiler
{
    public static TextProgram Compile(string source, IReadOnlyDictionary<string, int> symbols, TagDefinition[] tags) => new Parser(source, symbols, tags).Parse();

    private readonly record struct Token(string Text, int Line, int Column, bool Numeric = false)
    {
        public string Upper => Text.ToUpperInvariant();
    }

    private sealed class Lexer(string source)
    {
        private int _index, _line = 1, _column = 1;
        private char Current => _index < source.Length ? source[_index] : '\0';
        private char Peek => _index + 1 < source.Length ? source[_index + 1] : '\0';
        private void Advance() { if (Current == '\n') { _line++; _column = 1; } else _column++; _index++; }
        public Token Next()
        {
            while (true)
            {
                while (char.IsWhiteSpace(Current)) Advance();
                if (Current == '/' && Peek == '/') { while (Current is not '\0' and not '\n') Advance(); continue; }
                if (Current == '(' && Peek == '*')
                {
                    var line = _line; var column = _column; var nesting = 1; Advance(); Advance();
                    while (nesting > 0)
                    {
                        if (Current == '\0') throw new TextCompileException("Unterminated comment.", line, column);
                        if (Current == '(' && Peek == '*') { if (++nesting > 16) throw new TextCompileException("Comment nesting limit exceeded.", _line, _column); Advance(); Advance(); }
                        else if (Current == '*' && Peek == ')') { nesting--; Advance(); Advance(); }
                        else Advance();
                    }
                    continue;
                }
                break;
            }
            var start = _index; var l = _line; var c = _column;
            if (Current == '\0') return new("<EOF>", l, c);
            if (char.IsLetter(Current) || Current == '_')
            {
                while (char.IsLetterOrDigit(Current) || Current == '_') Advance();
                if (Current == '#')
                {
                    Advance(); if (Current == '-') Advance();
                    while (char.IsLetterOrDigit(Current) || Current == '.') Advance();
                    return new(source[start.._index], l, c, true);
                }
                return new(source[start.._index], l, c);
            }
            if (char.IsDigit(Current) || (Current == '.' && char.IsDigit(Peek)))
            {
                while (char.IsDigit(Current)) Advance();
                if (Current == '.') { Advance(); while (char.IsDigit(Current)) Advance(); }
                if (Current is 'E' or 'e') { Advance(); if (Current is '+' or '-') Advance(); while (char.IsDigit(Current)) Advance(); }
                return new(source[start.._index], l, c, true);
            }
            if ((Current == ':' && Peek == '=') || (Current == '<' && Peek is '=' or '>') || (Current == '>' && Peek == '=')) { Advance(); Advance(); return new(source[start.._index], l, c); }
            if (";()+-*/=<> ,".Contains(Current)) { Advance(); return new(source[start.._index], l, c); }
            throw new TextCompileException($"Unsupported character '{Current}'.", l, c);
        }
    }

    private sealed class Parser
    {
        private readonly Lexer _lexer;
        private readonly IReadOnlyDictionary<string, int> _symbols;
        private readonly TagDefinition[] _tags;
        private readonly List<TextInstruction> _code = [];
        private readonly List<SourceReference> _references = [];
        private Token _token;
        private int _depth;
        public Parser(string source, IReadOnlyDictionary<string, int> symbols, TagDefinition[] tags)
        {
            if (source.Length > DocumentLimits.MaxSourceLength) throw new TextCompileException("Source exceeds 1 MiB.", 1, 1);
            _symbols = symbols; _tags = tags; _lexer = new(source); _token = _lexer.Next();
        }
        private void Next() => _token = _lexer.Next();
        private bool Is(string text) => _token.Upper == text;
        private bool Take(string text) { if (!Is(text)) return false; Next(); return true; }
        private void Require(string text) { if (!Take(text)) Fail($"Expected '{text}', found '{_token.Text}'."); }
        private void Fail(string message) => throw new TextCompileException(message, _token.Line, _token.Column);
        private int Emit(TextOp op, double number = 0, int argument = 0) { _code.Add(new(op, number, argument)); return _code.Count - 1; }
        private void Patch(int index, int target) => _code[index] = _code[index] with { Argument = target };
        private int Resolve(Token token, bool write)
        {
            if (!_symbols.TryGetValue(token.Text, out var slot)) throw new TextCompileException($"Unknown tag '{token.Text}'.", token.Line, token.Column);
            var tag = _tags[slot];
            if (write && PlcAddress.TryParse(tag.Address, tag.Type, out var address) && address.IsInput) throw new TextCompileException($"Input tag '{tag.Name}' is read-only in a PLC program.", token.Line, token.Column);
            _references.Add(new(tag.Name, write ? "Write" : "Read", token.Line, token.Column)); return slot;
        }
        public TextProgram Parse()
        {
            Statements("<EOF>"); Require("<EOF>"); return new(_code.ToArray(), _references.ToArray());
        }
        private void Statements(params string[] terminators)
        {
            if (++_depth > 64) Fail("Statement nesting limit exceeded.");
            while (!terminators.Contains(_token.Upper))
            {
                if (Is("<EOF>")) Fail("Unexpected end of program.");
                if (Take(";")) continue;
                if (Take("IF")) { IfBody(); continue; }
                if (Take("WHILE"))
                {
                    var start = _code.Count; Boolean(Expression()); Require("DO"); var exit = Emit(TextOp.JumpFalse);
                    Statements("END_WHILE"); Require("END_WHILE"); Require(";"); Emit(TextOp.Jump, argument: start); Patch(exit, _code.Count); continue;
                }
                if (Take("REPEAT"))
                {
                    var start = _code.Count; Statements("UNTIL"); Require("UNTIL"); Boolean(Expression()); Emit(TextOp.JumpFalse, argument: start);
                    Require("END_REPEAT"); Require(";"); continue;
                }
                var target = _token; var slot = Resolve(target, true); Next(); Require(":=");
                var type = Expression();
                if ((_tags[slot].Type == PlcType.Bool) != (type == PlcType.Bool)) Fail("Assignment cannot mix BOOL and numeric values.");
                Emit(TextOp.Store, argument: slot); Require(";");
            }
            _depth--;
        }
        private void IfBody()
        {
            Boolean(Expression()); Require("THEN"); var falseJump = Emit(TextOp.JumpFalse);
            Statements("ELSE", "ELSIF", "END_IF");
            var exit = Emit(TextOp.Jump); Patch(falseJump, _code.Count);
            if (Take("ELSIF")) IfBody();
            else { if (Take("ELSE")) Statements("END_IF"); Require("END_IF"); Require(";"); }
            Patch(exit, _code.Count);
        }
        private void Boolean(PlcType type) { if (type != PlcType.Bool) Fail("A BOOL expression is required."); }
        private void Numeric(PlcType type) { if (type == PlcType.Bool) Fail("A numeric expression is required."); }
        private PlcType Expression(int precedence = 0)
        {
            if (++_depth > 128) Fail("Expression nesting limit exceeded.");
            var left = Unary();
            while (Priority(_token.Upper) > precedence)
            {
                var op = _token.Upper; var priority = Priority(op); Next(); var right = Expression(priority);
                if (op is "AND" or "OR" or "XOR") { Boolean(left); Boolean(right); Emit(op switch { "AND" => TextOp.And, "OR" => TextOp.Or, _ => TextOp.Xor }); left = PlcType.Bool; }
                else if (op is "=" or "<>" or ">" or "<" or ">=" or "<=")
                {
                    if ((left == PlcType.Bool) != (right == PlcType.Bool)) Fail("Comparison cannot mix BOOL and numeric values.");
                    if (left == PlcType.Bool && op is not "=" and not "<>") Fail("BOOL supports only equality comparisons.");
                    Emit(op switch { "=" => TextOp.Equal, "<>" => TextOp.NotEqual, ">" => TextOp.Greater, "<" => TextOp.Less, ">=" => TextOp.GreaterOrEqual, _ => TextOp.LessOrEqual }); left = PlcType.Bool;
                }
                else
                {
                    Numeric(left); Numeric(right); var result = left == PlcType.Real || right == PlcType.Real ? PlcType.Real : PlcType.DInt;
                    Emit(op switch { "+" => TextOp.Add, "-" => TextOp.Subtract, "*" => TextOp.Multiply, "/" => result == PlcType.Real ? TextOp.Divide : TextOp.IntegerDivide, _ => TextOp.Modulo }); left = result;
                }
            }
            _depth--; return left;
        }
        private PlcType Unary()
        {
            if (Take("NOT")) { var type = Expression(6); Boolean(type); Emit(TextOp.Not); return PlcType.Bool; }
            if (Take("-")) { var type = Expression(6); Numeric(type); Emit(TextOp.Negate); return type; }
            if (Take("+")) { var type = Expression(6); Numeric(type); return type; }
            if (Take("(")) { var type = Expression(); Require(")"); return type; }
            if (Is("TRUE") || Is("FALSE")) { Emit(TextOp.Constant, Is("TRUE") ? 1 : 0); Next(); return PlcType.Bool; }
            if (_token.Numeric)
            {
                if (!PlcValues.TryParse(_token.Text, out var value)) Fail("Invalid numeric or TIME literal.");
                var type = _token.Text.Contains('#') ? PlcType.Time : _token.Text.Contains('.') || _token.Text.Contains('e', StringComparison.OrdinalIgnoreCase) ? PlcType.Real : PlcType.DInt;
                Emit(TextOp.Constant, value); Next(); return type;
            }
            var token = _token; Next();
            if (Take("("))
            {
                var type = Expression(); Numeric(type);
                switch (token.Upper)
                {
                    case "ABS": Emit(TextOp.Abs); break;
                    case "SQRT": Emit(TextOp.Sqrt); type = PlcType.Real; break;
                    case "MIN": case "MAX":
                        Require(","); var other = Expression(); Numeric(other); if (other == PlcType.Real) type = other;
                        Emit(token.Upper == "MIN" ? TextOp.Min : TextOp.Max); break;
                    default: throw new TextCompileException($"Unsupported function '{token.Text}'. Supported: ABS, SQRT, MIN, MAX.", token.Line, token.Column);
                }
                Require(")"); return type;
            }
            var slot = Resolve(token, false); Emit(TextOp.Load, argument: slot); return _tags[slot].Type;
        }
        private static int Priority(string token) => token switch { "OR" => 1, "XOR" => 2, "AND" => 3, "=" or "<>" or ">" or "<" or ">=" or "<=" => 4, "+" or "-" => 5, "*" or "/" or "MOD" => 6, _ => 0 };
    }
}
