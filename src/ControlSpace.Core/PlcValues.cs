using System.Globalization;
using System.Text.RegularExpressions;

namespace ControlSpace.Core;

public static class PlcValues
{
    public static bool IsBoolean(PlcType type) => type == PlcType.Bool;
    public static double Normalize(PlcType type, double value)
    {
        if (!double.IsFinite(value)) throw new ArithmeticException("PLC values must be finite.");
        return type switch
        {
            PlcType.Bool when value is 0 or 1 => value,
            PlcType.Bool => throw new ArithmeticException("BOOL accepts only TRUE/FALSE or 1/0."),
            PlcType.Int when value >= short.MinValue && value <= short.MaxValue && value == Math.Truncate(value) => value,
            PlcType.DInt or PlcType.Time when value >= int.MinValue && value <= int.MaxValue && value == Math.Truncate(value) => value,
            PlcType.Real when float.IsFinite((float)value) => (float)value,
            _ => throw new ArithmeticException($"Value {value.ToString(CultureInfo.InvariantCulture)} is outside {type} range or is not integral.")
        };
    }

    public static bool TryParse(string text, out double value)
    {
        text = text.Trim();
        if (text.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) { value = 1; return true; }
        if (text.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) { value = 0; return true; }
        if (text.StartsWith("T#", StringComparison.OrdinalIgnoreCase) || text.StartsWith("TIME#", StringComparison.OrdinalIgnoreCase))
        {
            var body = text[(text.IndexOf('#') + 1)..];
            var match = Regex.Match(body, @"^(-?\d+(?:\.\d+)?)(ms|s|m|h|d)$", RegexOptions.IgnoreCase);
            if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                value = number * (match.Groups[2].Value.ToLowerInvariant() switch { "ms" => 1, "s" => 1000, "m" => 60000, "h" => 3600000, _ => 86400000 });
                return double.IsFinite(value) && value == Math.Truncate(value) && value >= int.MinValue && value <= int.MaxValue;
            }
            value = 0; return false;
        }
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    public static string Format(PlcType type, double value) => type switch
    {
        PlcType.Bool => value != 0 ? "TRUE" : "FALSE",
        PlcType.Time => $"T#{value.ToString("0", CultureInfo.InvariantCulture)}ms",
        PlcType.Real => value.ToString("0.###", CultureInfo.InvariantCulture),
        _ => value.ToString("0", CultureInfo.InvariantCulture)
    };

    public static bool IsIdentifier(string value) => Regex.IsMatch(value ?? "", @"^[A-Za-z_][A-Za-z0-9_]*$") && value.Length <= 128;
}

public readonly record struct PlcAddress(char Area, int StartBit, int BitLength)
{
    public bool IsInput => Area == 'I';
    public bool IsOutput => Area == 'Q';
    public bool Overlaps(PlcAddress other) => Area == other.Area && StartBit < other.StartBit + other.BitLength && other.StartBit < StartBit + BitLength;

    public static bool TryParse(string address, PlcType type, out PlcAddress result)
    {
        result = default;
        var match = Regex.Match(address.Trim().ToUpperInvariant(), @"^%?([IQM])([BWD]?)(\d+)(?:\.([0-7]))?$");
        if (!match.Success || !int.TryParse(match.Groups[3].Value, out var offset) || offset > 65535) return false;
        var width = match.Groups[2].Value;
        var bit = match.Groups[4];
        var length = type == PlcType.Bool ? 1 : type == PlcType.Int ? 16 : 32;
        if (type == PlcType.Bool && (width != "" || !bit.Success)) return false;
        if (type != PlcType.Bool && (bit.Success || width != (length == 16 ? "W" : "D"))) return false;
        result = new(match.Groups[1].Value[0], offset * 8 + (bit.Success ? int.Parse(bit.Value, CultureInfo.InvariantCulture) : 0), length);
        return true;
    }
}
