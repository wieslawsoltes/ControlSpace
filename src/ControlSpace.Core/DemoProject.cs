namespace ControlSpace.Core;

public static class DemoProject
{
    public static ControlProject Create()
    {
        Instruction I(string id, InstructionKind k, string tag, double p = 0, string aux = "") => new(id, k, tag, p, aux);
        var tags = new List<PlcTag>
        {
            new("Start_PB", PlcType.Bool, "%I0.0", 0, "Start conveyor"),
            new("Stop_PB", PlcType.Bool, "%I0.1", 0, "Stop conveyor (simulation only)"),
            new("Part_Sensor", PlcType.Bool, "%I0.2", 0, "Product detection"),
            new("Reset_Count", PlcType.Bool, "%I0.3", 0, "Reset production counter"),
            new("Motor_Run", PlcType.Bool, "%Q0.0", 0, "Conveyor motor enable"),
            new("Ready_Lamp", PlcType.Bool, "%Q0.1", 0, "Run-up complete"),
            new("Run_Delay", PlcType.Bool, "%M0.0", 0, "TON output"),
            new("Delay_ET", PlcType.Time, "%MD4", 0, "Elapsed run-up time"),
            new("Part_Count", PlcType.DInt, "%MD8", 0, "Rising-edge product count", true),
            new("Speed_Setpoint", PlcType.Real, "%MD12", 65, "Conveyor setpoint (%)", true),
            new("Speed_Actual", PlcType.Real, "%MD16", 0, "Simulated conveyor speed (%)")
        };
        var networks = new List<LadderNetwork>
        {
            new("net-start", "Conveyor start / stop", "Seal-in circuit • Start_PB or Motor_Run, inhibited by Stop_PB.",
                [[I("start",InstructionKind.Contact,"Start_PB"),I("stop",InstructionKind.NegatedContact,"Stop_PB")],
                 [I("hold",InstructionKind.Contact,"Motor_Run"),I("stop-hold",InstructionKind.NegatedContact,"Stop_PB")]], I("coil",InstructionKind.Coil,"Motor_Run")),
            new("net-delay", "Run-up delay", "Signal ready after the motor has been running for two seconds.",
                [[I("run-contact",InstructionKind.Contact,"Motor_Run")]],I("ton",InstructionKind.TimerOn,"Run_Delay",2000,"Delay_ET")),
            new("net-ready", "Ready indication", "Enable the ready indicator after the run-up time.",
                [[I("ready-contact",InstructionKind.Contact,"Run_Delay")]],I("ready-coil",InstructionKind.Coil,"Ready_Lamp")),
            new("net-count", "Production counter", "Count rising edges at the product sensor; Reset_Count resets the count.",
                [[I("part-contact",InstructionKind.Contact,"Part_Sensor")]],I("ctu",InstructionKind.CountUp,"Part_Count",0,"Reset_Count"))
        };
        return new(ControlProject.FormatId,1,"conveyor-demo","Conveyor_Line",0,tags,
            [new("main","Main",1,BlockLanguage.LAD,true,networks),
             new("speed","Speed_Control",2,BlockLanguage.SCL,true,[], "// Cyclic speed control — simulation only\nIF \"Motor_Run\" THEN\n    \"Speed_Actual\" := \"Speed_Setpoint\";\nELSE\n    \"Speed_Actual\" := 0;\nEND_IF;\n")],
            [new("plc","PLC_1",DeviceKind.Controller,"Generic CPU 1500 · simulation", "192.168.0.1",90,100,["CPU", "DI 16×24 V", "DQ 16×24 V"]),
             new("hmi","HMI_1",DeviceKind.Hmi,"Comfort panel · simulation","192.168.0.2",440,100,[]),
             new("io","IO_Station",DeviceKind.RemoteIo,"Remote I/O · simulation","192.168.0.3",770,100,["IM", "DI 8", "DQ 8"])],
            [new("link1","plc","hmi"),new("link2","hmi","io")],
            [new("overview","Overview",960,540,
                [new("title",HmiKind.Label,"CONVEYOR LINE / 01","",32,24,650,48),
                 new("subtitle",HmiKind.Label,"Production overview","",32,82,450,30,"#667C8C"),
                 new("motor-lamp",HmiKind.Lamp,"MOTOR RUNNING","Motor_Run",40,160,190,88),
                 new("ready-lamp",HmiKind.Lamp,"SYSTEM READY","Ready_Lamp",270,160,190,88),
                 new("speed-display",HmiKind.Gauge,"CONVEYOR SPEED","Speed_Actual",540,145,330,185),
                 new("count-display",HmiKind.Numeric,"PARTS PRODUCED","Part_Count",42,290,370,100),
                 new("start-button",HmiKind.Button,"START","Start_PB",42,438,180,58),
                 new("stop-button",HmiKind.Button,"STOP","Stop_PB",250,438,180,58,"#B65045")])]);
    }
}
