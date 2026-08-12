using System.Text.RegularExpressions;

namespace XrmEmulator.MetadataSync.Audit;

/// <summary>
/// Resolves <c>J-nnn</c> justification references in the audit document against a catalogue of
/// reasons kept in a separate markdown file (in practice the hand-written privilege log).
/// </summary>
/// <remarks>
/// The audit's <c>Justification</c> cell is opaque text as far as generation is concerned — it is
/// only ever carried over, never written — so referring to a reason by id instead of repeating prose
/// needs no change to the format. What it does need is a check that the ids resolve, which is what
/// this class provides: an id that no longer exists in the catalogue, or a reason nothing points at,
/// are both worth reporting.
/// </remarks>
public static partial class JustificationCatalogue
{
    [GeneratedRegex(@"\bJ-\d{1,4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReferencePattern();

    public sealed record Reason(string Id, string Summary);

    public sealed record ValidationResult(
        List<Reason> Defined,
        List<string> UnknownReferences,
        List<Reason> Unreferenced,
        int ReferencingRows);

    /// <summary>
    /// Reads reason ids out of a markdown file. Any table row whose first cell is a bare
    /// <c>J-nnn</c> is a reason; the longest remaining cell is used as its summary, which keeps the
    /// catalogue's column layout a free choice.
    /// </summary>
    public static List<Reason> Load(string markdown)
    {
        var reasons = new List<Reason>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith('|')) continue;

            var cells = SplitCells(line);
            if (cells.Count < 2) continue;

            var id = cells[0];
            if (!IsBareId(id)) continue;
            if (!seen.Add(id)) continue;

            var summary = cells.Skip(1)
                .OrderByDescending(c => c.Length)
                .FirstOrDefault() ?? string.Empty;

            reasons.Add(new Reason(id.ToUpperInvariant(), Collapse(summary)));
        }

        return reasons;
    }

    public static ValidationResult Validate(
        IEnumerable<SecurityRoleAuditDocument.Row> rows,
        List<Reason> defined)
    {
        var known = defined.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var referencingRows = 0;

        foreach (var row in rows)
        {
            if (!row.IsJustified) continue;

            var matches = ReferencePattern().Matches(row.Justification);
            if (matches.Count == 0) continue;

            referencingRows++;
            foreach (Match match in matches)
            {
                var id = match.Value.ToUpperInvariant();
                if (known.Contains(id)) referenced.Add(id);
                else unknown.Add(id);
            }
        }

        return new ValidationResult(
            defined,
            unknown.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList(),
            defined.Where(r => !referenced.Contains(r.Id)).ToList(),
            referencingRows);
    }

    private static bool IsBareId(string cell)
    {
        var match = ReferencePattern().Match(cell);
        return match.Success && match.Length == cell.Length;
    }

    private static List<string> SplitCells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];
        return trimmed.Split('|').Select(c => c.Trim()).ToList();
    }

    private static string Collapse(string value)
    {
        var single = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return single.Length <= 100 ? single : single[..97] + "…";
    }
}
