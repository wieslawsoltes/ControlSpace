using System.Text;
namespace ControlSpace.Engineering;

public readonly record struct SymbolToken(int Start, int Length, string Name, bool Quoted);
/// <summary>Source-preserving symbol inspection for drafts, including drafts that do not compile.</summary>
public static class SymbolText
{
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    { "IF", "THEN", "ELSE", "END_IF", "TRUE", "FALSE", "AND", "OR", "XOR", "NOT", "MOD" };
    public static IEnumerable<SymbolToken> Identifiers(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > 1_000_000) throw new ArgumentException("Source exceeds 1 MB.");
        int i = 0;
        while (i < source.Length)
        {
            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '/')
            { i += 2; while (i < source.Length && source[i] is not ('\r' or '\n')) i++; continue; }
            if (i + 1 < source.Length && source[i] == '(' && source[i + 1] == '*')
            { i += 2; while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == ')')) i++; i = Math.Min(source.Length, i + 2); continue; }
            int start = i;
            if (source[i] == '"')
            {
                i++; while (i < source.Length && source[i] != '"') i++;
                if (i < source.Length) { i++; yield return new(start, i - start, source[(start + 1)..(i - 1)], true); }
            }
            else if (char.IsDigit(source[i]) || source[i] == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1]))
            {
                while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.')) i++;
                if (i < source.Length && source[i] is 'e' or 'E') { i++; if (i < source.Length && source[i] is '+' or '-') i++; while (i < source.Length && char.IsDigit(source[i])) i++; }
            }
            else if (char.IsLetter(source[i]) || source[i] == '_')
            {
                i++; while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                string name = source[start..i]; if (!Keywords.Contains(name)) yield return new(start, i - start, name, false);
            }
            else i++;
        }
    }
    public static string Rename(string source, IReadOnlyDictionary<string, string> replacements)
    {
        var names = new Dictionary<string, string>(replacements, StringComparer.OrdinalIgnoreCase);
        var result = new StringBuilder(source.Length); int copied = 0;
        foreach (var token in Identifiers(source))
        {
            if (!names.TryGetValue(token.Name, out var next)) continue;
            result.Append(source, copied, token.Start - copied);
            if (token.Quoted || Keywords.Contains(next)) result.Append('"').Append(next).Append('"'); else result.Append(next);
            copied = token.Start + token.Length;
        }
        return result.Append(source, copied, source.Length - copied).ToString();
    }
}
