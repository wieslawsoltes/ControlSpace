using System.Globalization;
namespace ControlSpace.Core;

public enum HmiButtonAction { MomentaryInput, ActivateScreen, PreviousScreen, None }
/// <summary>One explicitly configured button action. Targets use stable screen IDs.</summary>
public sealed record HmiButtonBehavior(HmiButtonAction Action = HmiButtonAction.MomentaryInput, string TargetScreenId = "");
/// <summary>Display/input settings in process units. No implicit PLC scaling or forcing.</summary>
public sealed record HmiNumericOptions(bool Editable = false, double Minimum = 0, double Maximum = 100, int DecimalPlaces = 2, string Unit = "");

public static class HmiRuntimeOptions
{
    public static bool IsNumeric(HmiKind kind) => kind is HmiKind.Numeric or HmiKind.Gauge or HmiKind.Tank;
    public static HmiButtonAction Action(HmiObject item) => item.Button?.Action ?? HmiButtonAction.MomentaryInput;
    public static bool CanEnter(PlcTag tag) => tag.Type != PlcType.Bool &&
        (PlcValues.IsInput(tag.Address) || tag.Address.StartsWith("%M", StringComparison.OrdinalIgnoreCase));
    public static string? Error(HmiObject item, IReadOnlySet<string> screenIds, IReadOnlyDictionary<string, PlcTag> tags)
    {
        if (item.Button is { } button)
        {
            if (item.Kind != HmiKind.Button || !Enum.IsDefined(button.Action) || button.TargetScreenId is null)
                return "Button behavior is only valid on a button with a supported action.";
            if (button.Action == HmiButtonAction.ActivateScreen)
            {
                if (!screenIds.Contains(button.TargetScreenId)) return "The button target screen does not exist.";
            }
            else if (button.TargetScreenId.Length != 0) return "Only Activate screen can have a screen target.";
            if (button.Action != HmiButtonAction.MomentaryInput && !string.IsNullOrEmpty(item.Tag))
                return "Navigation and inactive buttons must not have a PLC tag binding.";
        }
        if (item.Numeric is { } numeric)
        {
            if (!IsNumeric(item.Kind)) return "Numeric display options are only valid on numeric displays, gauges and tanks.";
            if (!double.IsFinite(numeric.Minimum) || !double.IsFinite(numeric.Maximum) || numeric.Minimum >= numeric.Maximum ||
                !double.IsFinite(numeric.Maximum - numeric.Minimum) || numeric.DecimalPlaces is < 0 or > 9 ||
                numeric.Unit is null || numeric.Unit.Length > 24 || numeric.Unit.Any(char.IsControl))
                return "Use finite increasing limits, 0–9 decimal places and up to 24 printable unit characters.";
            if (numeric.Editable && item.Kind != HmiKind.Numeric) return "Only a numeric display can be configured for operator input.";
            if (!string.IsNullOrEmpty(item.Tag) && (!tags.TryGetValue(item.Tag, out var bound) || bound.Type == PlcType.Bool))
                return "Numeric display options require a compatible numeric tag.";
            if (numeric.Editable && !string.IsNullOrEmpty(item.Tag) &&
                (!tags.TryGetValue(item.Tag, out var tag) || !CanEnter(tag)))
                return "Numeric input requires a numeric input-image or marker tag, not an output-image tag.";
        }
        return null;
    }
    public static string Format(HmiObject item, double value)
    {
        if (!double.IsFinite(value)) return "—";
        if (item.Numeric is not { } options) return value.ToString(item.Kind == HmiKind.Gauge ? "0" : "0.##", CultureInfo.InvariantCulture) + (item.Kind == HmiKind.Gauge ? " %" : "");
        return value.ToString("F" + Math.Clamp(options.DecimalPlaces, 0, 9), CultureInfo.InvariantCulture) +
            (string.IsNullOrEmpty(options.Unit) ? "" : " " + options.Unit);
    }
    public static double Fraction(HmiObject item, double value)
    {
        double min = item.Numeric?.Minimum ?? 0, max = item.Numeric?.Maximum ?? 100;
        return double.IsFinite(value) && double.IsFinite(max - min) && max > min ? Math.Clamp((value - min) / (max - min), 0, 1) : 0;
    }
    public static bool IsOutOfRange(HmiObject item, double value) => item.Numeric is { } n && (!double.IsFinite(value) || value < n.Minimum || value > n.Maximum);
    /// <summary>Invariant decimal/exponent input. Excess fractional precision is rejected, never silently rounded.</summary>
    public static double ParseInput(HmiObject item, PlcTag tag, string text)
    {
        var options = item.Numeric;
        if (item.Kind != HmiKind.Numeric || options?.Editable != true || !CanEnter(tag)) throw new InvalidOperationException("This object is not an editable numeric input.");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 128 || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !PlcValues.IsValid(tag.Type, value))
            throw new ArgumentException("Enter a finite number valid for the bound PLC data type. Use a decimal point, not grouping separators.");
        if (!double.IsFinite(options.Minimum) || !double.IsFinite(options.Maximum) || options.Minimum >= options.Maximum ||
            !double.IsFinite(options.Maximum - options.Minimum) || options.DecimalPlaces is < 0 or > 9 || value < options.Minimum || value > options.Maximum)
            throw new ArgumentException($"Enter a value from {options.Minimum.ToString(CultureInfo.InvariantCulture)} to {options.Maximum.ToString(CultureInfo.InvariantCulture)}.");
        if (Math.Round(value, options.DecimalPlaces) != value) throw new ArgumentException($"Use at most {options.DecimalPlaces} decimal places. The value was not changed.");
        return value;
    }
}
