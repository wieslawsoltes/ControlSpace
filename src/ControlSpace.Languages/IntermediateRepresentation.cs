using ControlSpace.Core;
namespace ControlSpace.Languages;

public abstract record Expression(PlcType Type)
{
    public abstract double Evaluate(double[] values);
}
public sealed record LiteralExpression(double Value, PlcType ValueType) : Expression(ValueType)
{
    public override double Evaluate(double[] values) => Value;
}
public sealed record TagExpression(int Slot, PlcType TagType) : Expression(TagType)
{
    public override double Evaluate(double[] values) => values[Slot];
}
public sealed record UnaryExpression(string Operator, Expression Operand, PlcType ResultType) : Expression(ResultType)
{
    public override double Evaluate(double[] values) => Operator switch
    {
        "NOT" => Operand.Evaluate(values) == 0 ? 1 : 0,
        "-" => -Operand.Evaluate(values),
        "+" => Operand.Evaluate(values),
        _ => throw new InvalidOperationException("Unknown unary operator.")
    };
}
public sealed record BinaryExpression(string Operator, Expression Left, Expression Right, PlcType ResultType) : Expression(ResultType)
{
    public override double Evaluate(double[] values)
    {
        double a = Left.Evaluate(values), b = Right.Evaluate(values);
        return Operator switch
        {
            "+" => a + b, "-" => a - b, "*" => a * b,
            "/" => b == 0 ? throw new ArithmeticException("Division by zero.") : a / b,
            "MOD" => b == 0 ? throw new ArithmeticException("Modulo by zero.") : a % b,
            "AND" => a != 0 && b != 0 ? 1 : 0, "OR" => a != 0 || b != 0 ? 1 : 0,
            "XOR" => (a != 0) != (b != 0) ? 1 : 0,
            "=" => a == b ? 1 : 0, "<>" => a != b ? 1 : 0,
            ">" => a > b ? 1 : 0, "<" => a < b ? 1 : 0,
            ">=" => a >= b ? 1 : 0, "<=" => a <= b ? 1 : 0,
            _ => throw new InvalidOperationException("Unknown binary operator.")
        };
    }
}
public abstract record Statement;
public sealed record AssignmentStatement(int Slot, Expression Value) : Statement;
public sealed record IfStatement(Expression Condition, IReadOnlyList<Statement> Then, IReadOnlyList<Statement> Else) : Statement;
public sealed record CompiledContact(string Id, InstructionKind Kind, int Slot, double Parameter);
public sealed record CompiledOutput(string Id, InstructionKind Kind, int Slot, double Parameter, int AuxiliarySlot);
public sealed record CompiledNetwork(string Id, IReadOnlyList<IReadOnlyList<CompiledContact>> Branches, CompiledOutput Output);
public sealed record CompiledBlock(string Id, IReadOnlyList<CompiledNetwork> Networks, IReadOnlyList<Statement> Statements);
public sealed record CompiledProgram(ControlProject Project, IReadOnlyDictionary<string, int> Symbols, IReadOnlyList<CompiledBlock> Blocks);
public sealed record CompilationResult(CompiledProgram? Program, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Program is not null && !Diagnostics.Any(d => d.Severity == Severity.Error);
}
