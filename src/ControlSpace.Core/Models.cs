namespace ControlSpace.Core;

public enum PlcType { Bool, Int, DInt, Real, Time }
public enum ProgramLanguage { LAD, FBD, ST }
public enum ContactKind { NormallyOpen, NormallyClosed, RisingEdge, FallingEdge, Equal, NotEqual, Greater, Less, GreaterOrEqual, LessOrEqual }
public enum ActionKind { Coil, Set, Reset, TON, TOF, TP, CTU, Move, Add, Subtract, Multiply, Divide }
public enum DeviceKind { Controller, DigitalInput, DigitalOutput, AnalogInput, AnalogOutput, Hmi, Switch, Drive }
public enum HmiKind { Text, Button, Lamp, Numeric, Bar, Tank, Motor, Rectangle }
public enum Severity { Info, Warning, Error }

public sealed record TagDefinition
{
    public string Name { get; init; } = "NewTag";
    public PlcType Type { get; init; }
    public string Address { get; init; } = "";
    public double InitialValue { get; init; }
    public bool Retain { get; init; }
    public string Comment { get; init; } = "";
}

public sealed record Contact
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ContactKind Kind { get; init; }
    public string Operand { get; init; } = "Start";
    public string CompareTo { get; init; } = "0";
}

public sealed record LadderAction
{
    public ActionKind Kind { get; init; }
    public string Target { get; init; } = "ConveyorRun";
    public string InputA { get; init; } = "0";
    public string InputB { get; init; } = "0";
    public double Preset { get; init; } = 1000;
    public string ResetTag { get; init; } = "";
    public string AuxiliaryTag { get; init; } = "";
}

/// <summary>Parallel paths (OR), each containing series contacts (AND), followed by one instruction.</summary>
public sealed record LadderNetwork
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; init; } = "New network";
    public string Comment { get; init; } = "";
    public Contact[][] Paths { get; init; } = [[]];
    public LadderAction Action { get; init; } = new();
}

public sealed record ProgramBlock
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Main [OB1]";
    public ProgramLanguage Language { get; init; }
    public bool Enabled { get; init; } = true;
    public string Comment { get; init; } = "";
    public LadderNetwork[] Networks { get; init; } = [];
    public string Source { get; init; } = "// Structured text\n";
}

public sealed record DeviceModule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "DI 16x24VDC";
    public DeviceKind Kind { get; init; } = DeviceKind.DigitalInput;
    public int Slot { get; init; } = 2;
    public int Channels { get; init; } = 16;
}

public sealed record DeviceDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "PLC_1";
    public DeviceKind Kind { get; init; }
    public string Model { get; init; } = "CS-1500 CPU 1511-1 PN";
    public string IpAddress { get; init; } = "192.168.0.1";
    public double X { get; init; } = 100;
    public double Y { get; init; } = 100;
    public DeviceModule[] Modules { get; init; } = [];
}

public sealed record NetworkConnection(Guid From, Guid To, string Name = "PN/IE_1");

public sealed record HmiObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public HmiKind Kind { get; init; }
    public string Name { get; init; } = "Object";
    public string Text { get; init; } = "Text";
    public string Tag { get; init; } = "";
    public double X { get; init; } = 40;
    public double Y { get; init; } = 40;
    public double Width { get; init; } = 120;
    public double Height { get; init; } = 44;
    public string Fill { get; init; } = "#008A95";
    public double Maximum { get; init; } = 100;
}

public sealed record HmiScreen
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Overview";
    public double Width { get; init; } = 960;
    public double Height { get; init; } = 540;
    public HmiObject[] Objects { get; init; } = [];
}

public sealed record AlarmDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Tag { get; init; } = "GuardClosed";
    public bool ActiveWhen { get; init; }
    public string Message { get; init; } = "Guard open";
    public Severity Severity { get; init; } = Severity.Warning;
}

/// <summary>Document snapshots are treated as immutable. Editing APIs replace arrays rather than modifying them.</summary>
public sealed record ProjectDocument
{
    public int FormatVersion { get; init; } = 1;
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "New project";
    public string Author { get; init; } = "";
    public string Comment { get; init; } = "";
    public TagDefinition[] Tags { get; init; } = [];
    public ProgramBlock[] Blocks { get; init; } = [];
    public DeviceDefinition[] Devices { get; init; } = [];
    public NetworkConnection[] Connections { get; init; } = [];
    public HmiScreen[] Screens { get; init; } = [];
    public AlarmDefinition[] Alarms { get; init; } = [];
}

public sealed record Diagnostic(Severity Severity, string Code, string Message, string Location = "", int Line = 0, int Column = 0);
public sealed record CrossReference(string Tag, string Block, Guid NetworkId, string Access);

public static class DocumentLimits
{
    public const int MaxBytes = 16 * 1024 * 1024;
    public const int MaxTags = 10000;
    public const int MaxBlocks = 256;
    public const int MaxNetworks = 10000;
    public const int MaxContacts = 100000;
    public const int MaxSourceLength = 1024 * 1024;
}
