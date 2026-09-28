namespace ControlSpace.Core;

public static class DemoProject
{
    public static ProjectDocument Create()
    {
        Contact C(string tag, ContactKind kind = ContactKind.NormallyOpen) => new() { Operand = tag, Kind = kind };
        TagDefinition T(string name, PlcType type, string address, double initial, string comment) => new() { Name = name, Type = type, Address = address, InitialValue = initial, Comment = comment };
        var plc = new DeviceDefinition { Name = "PLC_1", X = 110, Y = 150, Modules = [new() { Slot = 2 }, new() { Slot = 3, Name = "DQ 16x24VDC", Kind = DeviceKind.DigitalOutput }, new() { Slot = 4, Name = "AI 4xU/I", Kind = DeviceKind.AnalogInput, Channels = 4 }] };
        var hmi = new DeviceDefinition { Name = "HMI_1", Kind = DeviceKind.Hmi, Model = "CS Comfort Panel 7 inch", IpAddress = "192.168.0.2", X = 470, Y = 150 };
        var drive = new DeviceDefinition { Name = "Drive_1", Kind = DeviceKind.Drive, Model = "Generic variable-speed drive", IpAddress = "192.168.0.3", X = 810, Y = 150 };
        HmiObject O(HmiKind kind, string name, string text, string tag, double x, double y, double w, double h, string fill = "#008A95") => new() { Kind = kind, Name = name, Text = text, Tag = tag, X = x, Y = y, Width = w, Height = h, Fill = fill };
        return new()
        {
            Name = "Conveyor_Line", Author = "ControlSpace", Comment = "Conveyor start/stop, delayed ready, part counting and HMI. Local simulation only.",
            Tags = [
                T("Start", PlcType.Bool, "%I0.0", 0, "Start conveyor pushbutton"),
                T("Stop", PlcType.Bool, "%I0.1", 0, "Stop request"),
                T("GuardClosed", PlcType.Bool, "%I0.2", 1, "Simulated guard contact — not a safety function"),
                T("PartSensor", PlcType.Bool, "%I0.3", 0, "Photoelectric part sensor"),
                T("ResetCounter", PlcType.Bool, "%I0.4", 0, "Reset batch count"),
                T("ConveyorRun", PlcType.Bool, "%Q0.0", 0, "Conveyor motor command"),
                T("SystemReady", PlcType.Bool, "%Q0.1", 0, "Delayed ready indicator"),
                T("BatchFull", PlcType.Bool, "%Q0.2", 0, "Batch preset reached"),
                T("BatchCount", PlcType.DInt, "%MD10", 0, "Parts in current batch"),
                T("StartElapsed", PlcType.Time, "%MD14", 0, "Start delay elapsed time"),
                T("BeltSpeed", PlcType.Real, "%MD18", 0, "Conveyor speed in percent"),
                T("SpeedSetpoint", PlcType.Real, "%MD22", 55, "Operator speed setpoint")
            ],
            Blocks = [
                new() { Name = "Main [OB1]", Comment = "Main program sweep (cycle)", Networks = [
                    new() { Title = "Conveyor start / stop — seal-in circuit", Comment = "Start or hold the motor while the guard is closed and stop is not requested.", Paths = [[C("Start"), C("Stop", ContactKind.NormallyClosed), C("GuardClosed")], [C("ConveyorRun"), C("Stop", ContactKind.NormallyClosed), C("GuardClosed")]], Action = new() { Target = "ConveyorRun" } },
                    new() { Title = "Start delay — system ready", Comment = "TON on-delay timer. The ready indicator turns on after 2 seconds.", Paths = [[C("ConveyorRun")]], Action = new() { Kind = ActionKind.TON, Target = "SystemReady", Preset = 2000, AuxiliaryTag = "StartElapsed" } },
                    new() { Title = "Batch counter — count incoming parts", Comment = "CTU increments on a rising edge. ResetCounter clears the count.", Paths = [[C("PartSensor"), C("ConveyorRun")]], Action = new() { Kind = ActionKind.CTU, Target = "BatchCount", Preset = 10, ResetTag = "ResetCounter", AuxiliaryTag = "BatchFull" } }
                ] },
                new() { Name = "SpeedControl [FC1]", Language = ProgramLanguage.ST, Comment = "Bounded structured-text program", Source = "// Conveyor speed control\n// Executed after Main during each simulated scan.\n\nIF ConveyorRun THEN\n    BeltSpeed := SpeedSetpoint;\nELSE\n    BeltSpeed := 0.0;\nEND_IF;\n" }
            ],
            Devices = [plc, hmi, drive], Connections = [new(plc.Id, hmi.Id), new(plc.Id, drive.Id)],
            Screens = [new() { Name = "Overview", Objects = [
                O(HmiKind.Rectangle, "Header", "", "", 0, 0, 960, 75, "#263E4C"),
                O(HmiKind.Text, "Title", "CONVEYOR LINE  /  01", "", 28, 16, 520, 42, "#FFFFFF"),
                O(HmiKind.Text, "Subtitle", "Production overview", "", 32, 95, 440, 36, "#263E4C"),
                O(HmiKind.Motor, "ConveyorMotor", "M1", "ConveyorRun", 100, 185, 150, 125),
                O(HmiKind.Bar, "SpeedBar", "BELT SPEED", "BeltSpeed", 315, 195, 350, 100),
                O(HmiKind.Numeric, "Counter", "PARTS", "BatchCount", 730, 180, 170, 110),
                O(HmiKind.Lamp, "ReadyLamp", "System ready", "SystemReady", 110, 340, 210, 45),
                O(HmiKind.Lamp, "GuardLamp", "Guard closed", "GuardClosed", 360, 340, 210, 45),
                O(HmiKind.Button, "StartButton", "START", "Start", 80, 440, 200, 54, "#168250"),
                O(HmiKind.Button, "StopButton", "STOP", "Stop", 320, 440, 200, 54, "#B43A3A"),
                O(HmiKind.Button, "ResetButton", "RESET COUNT", "ResetCounter", 560, 440, 200, 54)
            ] }],
            Alarms = [new() { Tag = "GuardClosed", ActiveWhen = false, Message = "Simulated guard is open. Conveyor command is inhibited." }, new() { Tag = "BatchFull", ActiveWhen = true, Message = "Batch preset reached. Reset the counter to begin another batch.", Severity = Severity.Info }]
        };
    }
}
