namespace ControlSpace.Core;

public enum PlcType { Bool, Int, DInt, Real, Time }
public enum BlockLanguage { LAD, SCL }
public enum InstructionKind { Contact, NegatedContact, RisingEdge, FallingEdge, Greater, Less, Equal, Coil, SetCoil, ResetCoil, TimerOn, TimerOff, Pulse, CountUp, Move }
public enum DeviceKind { Controller, Hmi, RemoteIo, Switch }
public enum HmiKind { Label, Button, Lamp, Tank, Numeric, Gauge, Rectangle, Ellipse, Line }
public enum Severity { Info, Warning, Error }

public sealed record PlcTag(string Name, PlcType Type, string Address, double InitialValue = 0, string Comment = "", bool Retain = false);
public sealed record Instruction(string Id, InstructionKind Kind, string Tag, double Parameter = 0, string Auxiliary = "")
{
    public static Instruction Create(InstructionKind kind, string tag, double parameter = 0, string auxiliary = "") => new(Guid.NewGuid().ToString("N"), kind, tag, parameter, auxiliary);
}
public sealed record LadderNetwork(string Id, string Title, string Comment, List<List<Instruction>> Branches, Instruction Output)
{
    public static LadderNetwork Create(string title, List<List<Instruction>> branches, Instruction output) => new(Guid.NewGuid().ToString("N"), title, "", branches, output);
}
public sealed record ProgramBlock(string Id, string Name, int Number, BlockLanguage Language, bool Cyclic, List<LadderNetwork> Networks, string Source = "");
public sealed record Device(string Id, string Name, DeviceKind Kind, string Model, string IpAddress, double X, double Y, List<string> Modules);
public sealed record NetworkLink(string Id, string From, string To, string Subnet = "PN/IE_1");
public sealed record HmiObject(string Id, HmiKind Kind, string Text, string Tag, double X, double Y, double Width, double Height, string Color = "#008C95");
public sealed record HmiScreen(string Id, string Name, double Width, double Height, List<HmiObject> Objects);
public sealed record ControlProject(string Format, int Version, string Id, string Name, long Revision, List<PlcTag> Tags, List<ProgramBlock> Blocks, List<Device> Devices, List<NetworkLink> Links, List<HmiScreen> Screens)
{
    public const string FormatId = "controlspace.project";
    public const int CurrentVersion = 1;
}
public sealed record Diagnostic(Severity Severity, string Code, string Message, string Location = "", int Line = 0, int Column = 0);
public readonly record struct PointD(double X, double Y);
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public bool Contains(double x, double y) => x >= X && y >= Y && x <= X + Width && y <= Y + Height;
}

public static class PlcValues
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static bool IsValid(PlcType type, double value) => double.IsFinite(value) && (type switch
    {
        PlcType.Bool => value is 0 or 1,
        PlcType.Int => value is >= short.MinValue and <= short.MaxValue && value == Math.Truncate(value),
        PlcType.DInt => value is >= int.MinValue and <= int.MaxValue && value == Math.Truncate(value),
        PlcType.Time => value is >= 0 and <= int.MaxValue && value == Math.Truncate(value),
        PlcType.Real => value is >= -float.MaxValue and <= float.MaxValue,
        _ => false
    });
    public static string Format(PlcType type, double value) => type switch
    {
        PlcType.Bool => value != 0 ? "TRUE" : "FALSE",
        PlcType.Time => $"T#{value:0}ms",
        _ => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
    };
    public static bool IsInput(string address) => address.StartsWith("%I", StringComparison.OrdinalIgnoreCase);
    public static bool IsOutput(string address) => address.StartsWith("%Q", StringComparison.OrdinalIgnoreCase);
}
