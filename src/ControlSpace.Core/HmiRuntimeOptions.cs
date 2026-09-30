using System.Globalization;
namespace ControlSpace.Core;

public enum HmiIoMode { Output, Input, InputOutput }
public enum HmiButtonAction { Momentary, SetBit, ResetBit, ToggleBit, SetValue, ActivateScreen, PreviousScreen }

/// <summary>Optional, immutable runtime configuration. Null on an object preserves legacy behavior.</summary>
public sealed record HmiRuntimeOptions
{
    public HmiIoMode IoMode { get; init; } = HmiIoMode.Output;
    public HmiButtonAction Action { get; init; } = HmiButtonAction.Momentary;
    public string ScreenId { get; init; } = "";
    public double WriteValue { get; init; }
    public double Minimum { get; init; }
    public double Maximum { get; init; } = 100;
    public int DecimalPlaces { get; init; } = 2;
    public string Unit { get; init; } = "";
}

/// <summary>Shared authoring/import/runtime rules. No parsing or execution of scripts.</summary>
public static class HmiRuntimeRules
{
    public static HmiButtonAction Action(HmiObject o) => o.Runtime?.Action ?? HmiButtonAction.Momentary;
    public static bool IsNumeric(HmiKind kind) => kind is HmiKind.Numeric or HmiKind.Tank or HmiKind.Gauge;
    public static bool IsNumericInput(HmiObject o) => o.Kind == HmiKind.Numeric && o.Runtime?.IoMode is HmiIoMode.Input or HmiIoMode.InputOutput;
    public static bool IsWritable(PlcTag t) => PlcValues.IsInput(t.Address) || t.Address.StartsWith("%M", StringComparison.OrdinalIgnoreCase);
    public static bool IsInteractive(HmiObject o) => o.Kind == HmiKind.Button || IsNumericInput(o);
    public static bool Accepts(HmiObject o, PlcTag t) => o.Kind switch
    {
        HmiKind.Button => Action(o) switch
        {
            HmiButtonAction.Momentary => t.Type == PlcType.Bool && PlcValues.IsInput(t.Address),
            HmiButtonAction.SetBit or HmiButtonAction.ResetBit or HmiButtonAction.ToggleBit => t.Type == PlcType.Bool && IsWritable(t),
            HmiButtonAction.SetValue => IsWritable(t),
            _ => false
        },
        HmiKind.Lamp => t.Type == PlcType.Bool,
        HmiKind.Numeric => t.Type != PlcType.Bool && (!IsNumericInput(o) || IsWritable(t)),
        HmiKind.Gauge or HmiKind.Tank => t.Type != PlcType.Bool,
        _ => false
    };

    // Return one precise diagnostic per object without allocating per-object indexes.
    public static string? Validate(HmiObject o, Func<string, PlcTag?> findTag, IReadOnlySet<string> screens)
    {
        var r = o.Runtime;
        if (r is null) return null;
        if (o.Tag is null || r.ScreenId is null || r.Unit is null) return "HMI runtime strings cannot be null.";
        if (!Enum.IsDefined(r.Action) || !Enum.IsDefined(r.IoMode)) return "Unknown HMI runtime action or I/O mode.";
        if (!double.IsFinite(r.WriteValue)) return "The button write value must be finite.";
        if (!double.IsFinite(r.Minimum) || !double.IsFinite(r.Maximum) || r.Minimum >= r.Maximum ||
            !double.IsFinite(r.Maximum - r.Minimum)) return "HMI limits must be finite and minimum must be below maximum.";
        if (r.DecimalPlaces is < 0 or > 6 || r.Unit.Length > 24 || r.Unit.Any(char.IsControl)) return "Use 0–6 decimal places and at most 24 printable unit characters.";
        if (o.Kind != HmiKind.Button && !IsNumeric(o.Kind)) return "Runtime options are supported only by buttons and numeric displays.";
        if (o.Kind != HmiKind.Button && (r.Action != HmiButtonAction.Momentary || r.WriteValue != 0 || r.ScreenId.Length != 0)) return "Button actions cannot be assigned to numeric displays.";
        if (o.Kind != HmiKind.Numeric && r.IoMode != HmiIoMode.Output) return "Input modes require a numeric I/O field.";
        if (o.Kind == HmiKind.Button)
        {
            if (r.Minimum != 0 || r.Maximum != 100 || r.DecimalPlaces != 2 || r.Unit.Length != 0) return "Numeric formatting belongs to numeric displays, not buttons.";
            if (r.Action != HmiButtonAction.SetValue && r.WriteValue != 0) return "A write constant requires the SetValue action.";
            if (r.Action == HmiButtonAction.ActivateScreen)
                return o.Tag.Length != 0 ? "A screen-navigation button must be unbound." : !screens.Contains(r.ScreenId) ? "Choose an existing target screen." : null;
            if (r.ScreenId.Length != 0) return "A target screen requires the ActivateScreen action.";
            if (r.Action == HmiButtonAction.PreviousScreen) return o.Tag.Length == 0 ? null : "A Back button must be unbound.";
        }
        if (o.Tag.Length == 0)
            return IsNumericInput(o) || o.Kind == HmiKind.Button && r.Action != HmiButtonAction.Momentary ? "Choose a writable tag for this runtime control." : null;
        var tag = findTag(o.Tag);
        if (tag is null || !Accepts(o, tag)) return "Incompatible HMI binding. Writes require input or marker tags; physical-style output tags are read-only.";
        if (o.Kind == HmiKind.Button && r.Action == HmiButtonAction.SetValue && !PlcValues.IsValid(tag.Type, r.WriteValue)) return "The write constant cannot be represented by the selected tag type.";
        if (IsNumericInput(o))
        {
            double typeMin = tag.Type switch { PlcType.Int => short.MinValue, PlcType.DInt => int.MinValue, PlcType.Time => 0, _ => -float.MaxValue };
            double typeMax = tag.Type switch { PlcType.Int => short.MaxValue, PlcType.DInt => int.MaxValue, PlcType.Time => int.MaxValue, _ => float.MaxValue };
            double low = Math.Max(typeMin, r.Minimum), high = Math.Min(typeMax, r.Maximum);
            double scale = tag.Type == PlcType.Real ? Math.Pow(10, r.DecimalPlaces) : 1;
            if (low > high || Math.Ceiling(low * scale) > Math.Floor(high * scale)) return "The configured limits contain no representable input value.";
        }
        return null;
    }

    public static double ParseInput(HmiObject o, PlcTag tag, string text)
    {
        if (!IsNumericInput(o) || !Accepts(o, tag) || o.Runtime is not { } r) throw new InvalidOperationException("This object is not a writable numeric I/O field.");
        if (!double.IsFinite(r.Minimum) || !double.IsFinite(r.Maximum) || r.Minimum >= r.Maximum ||
            r.DecimalPlaces is < 0 or > 6 || !string.Equals(o.Tag, tag.Name, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid numeric input configuration or tag association.");
        if (text is null || text.Length > 80 || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            throw new ArgumentException("Enter a finite number using a dot as the decimal separator (no unit or grouping separators).");
        if (!PlcValues.IsValid(tag.Type, value)) throw new ArgumentException("The value is outside the tag type's range or is not an integer for an integer/TIME tag.");
        if (value < r.Minimum || value > r.Maximum) throw new ArgumentException($"Enter a value between {r.Minimum.ToString(CultureInfo.InvariantCulture)} and {r.Maximum.ToString(CultureInfo.InvariantCulture)}.");
        if (r.DecimalPlaces is < 0 or > 6 || Math.Round(value, r.DecimalPlaces) != value) throw new ArgumentException($"Use no more than {r.DecimalPlaces} decimal places.");
        return value;
    }

    public static string Format(HmiObject o, double value, bool runtime = false)
    {
        if (runtime && o.Runtime?.IoMode == HmiIoMode.Input) return "Enter value…";
        if (!double.IsFinite(value)) return "—";
        if (o.Runtime is not { } r) return value.ToString(o.Kind == HmiKind.Gauge ? "0" : "0.##", CultureInfo.InvariantCulture) + (o.Kind == HmiKind.Gauge ? " %" : "");
        return value.ToString("F" + Math.Clamp(r.DecimalPlaces, 0, 6), CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(r.Unit) ? "" : " " + r.Unit);
    }
    public static double Fraction(HmiObject o, double value)
    {
        double low = o.Runtime?.Minimum ?? 0, high = o.Runtime?.Maximum ?? 100;
        if (!double.IsFinite(value) || !double.IsFinite(high - low) || high <= low) return 0;
        return Math.Clamp((value - low) / (high - low), 0, 1);
    }
}
