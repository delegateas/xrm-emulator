using System.Text;
using Microsoft.Crm.Sdk.Messages;
using XrmEmulator.MetadataSync.Readers;

namespace XrmEmulator.MetadataSync.Audit;

/// <summary>
/// The append-only privilege audit document.
/// </summary>
/// <remarks>
/// The point of this file is the <c>Justification</c> column, which is written by hand and must
/// survive every regeneration. So the generator never deletes a row and never touches that column:
/// a privilege that disappears from the environment is marked <c>removed &lt;date&gt;</c> and kept,
/// because "this was granted and then withdrawn" is itself part of the audit trail.
///
/// One row is one <c>roleprivileges</c> grant — the unit an administrator actually decides on — not
/// one entity. A single privilege reaches many entities (the generic Activity privileges cover every
/// activity type installed in the org), and listing those per entity inflates one decision into
/// dozens of rows that all need the same justification. The entities a grant reaches are listed in
/// the <c>Applies to</c> column, which the generator does maintain.
///
/// Everything outside the two generated regions (the summary block and the privilege tables) is
/// preserved verbatim, so notes can be added to the file without the next run eating them.
/// </remarks>
public static class SecurityRoleAuditDocument
{
    public const string SummaryStart = "<!-- metadatasync:summary:start -->";
    public const string SummaryEnd = "<!-- metadatasync:summary:end -->";
    private const string ChangeLogHeading = "## Change log";
    private const string PrivilegesHeading = "## Privileges";

    /// <summary>Entities named in the <c>Applies to</c> cell before it collapses to a count.</summary>
    private const int AppliesToPreview = 6;

    // Rendered in this order; anything unrecognised is appended alphabetically after these.
    private static readonly string[] AccessOrder =
        ["Create", "Read", "Write", "Delete", "Append", "AppendTo", "Assign", "Share"];

    public sealed record Row
    {
        public required string Role { get; init; }

        /// <summary>The Dataverse privilege name, e.g. <c>prvCreateActivity</c>.</summary>
        public required string Privilege { get; init; }

        /// <summary>Access right the privilege confers, or empty for a task-based privilege.</summary>
        public required string Access { get; init; }

        public required string Depth { get; init; }

        /// <summary>Rendered list of entities the privilege reaches. Maintained by the generator.</summary>
        public required string AppliesTo { get; init; }

        public string Justification { get; init; } = string.Empty;
        public required string FirstSeen { get; init; }
        public required string Status { get; init; }

        /// <summary>
        /// Role + privilege name. Deliberately excludes <see cref="AppliesTo"/> and
        /// <see cref="Depth"/>: a managed solution installing another activity type must update the
        /// existing row, not retire it and lose its justification.
        /// </summary>
        public string Key => $"{Role} {Privilege}";

        public bool IsRemoved => Status.StartsWith("removed", StringComparison.OrdinalIgnoreCase);
        public bool IsJustified => !string.IsNullOrWhiteSpace(Justification);
    }

    public sealed record Change(string Kind, string Role, string Privilege, string Access, string Detail);

    public sealed record ParsedDocument(
        List<Row> Rows,
        string FreeText,
        string ChangeLog,
        bool LooksLikeAuditFile,
        bool IsEntityKeyedFormat);

    public sealed record MergeResult(
        List<Row> Rows,
        List<Change> Changes,
        List<string> RolesNotFound);

    // ──────────────────────────────────────────────────────────
    // Parse
    // ──────────────────────────────────────────────────────────

    public static ParsedDocument Parse(string markdown)
    {
        var rows = new List<Row>();
        var freeText = new StringBuilder();
        var changeLog = new StringBuilder();

        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        // Regions: 0 = header/summary (dropped, regenerated), 1 = free text,
        //          2 = change log (preserved), 3 = privilege tables (parsed)
        var region = 0;
        var inSummary = false;
        var currentRole = string.Empty;
        var looksLikeAuditFile = false;
        var isEntityKeyedFormat = false;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();

            if (line.Trim() == SummaryStart) { inSummary = true; continue; }
            if (line.Trim() == SummaryEnd) { inSummary = false; region = 1; continue; }
            if (inSummary) continue;

            if (line.StartsWith(ChangeLogHeading, StringComparison.Ordinal)) { region = 2; continue; }
            if (line.StartsWith(PrivilegesHeading, StringComparison.Ordinal))
            {
                region = 3;
                looksLikeAuditFile = true;
                continue;
            }

            switch (region)
            {
                case 0:
                    // Title and generated preamble — regenerated, so dropped.
                    break;
                case 1:
                    freeText.AppendLine(line);
                    break;
                case 2:
                    changeLog.AppendLine(line);
                    break;
                case 3:
                    if (line.StartsWith("### ", StringComparison.Ordinal))
                    {
                        currentRole = line[4..].Trim();
                        break;
                    }

                    // The first generation of this file keyed rows by entity. Detect it so the
                    // caller can refuse to overwrite rather than silently drop justifications.
                    if (line.StartsWith("| Entity ", StringComparison.OrdinalIgnoreCase))
                    {
                        isEntityKeyedFormat = true;
                        break;
                    }

                    var row = TryParseRow(currentRole, line);
                    if (row is not null) rows.Add(row);
                    break;
            }
        }

        return new ParsedDocument(
            rows,
            freeText.ToString().Trim('\n'),
            changeLog.ToString().Trim('\n'),
            looksLikeAuditFile,
            isEntityKeyedFormat);
    }

    private static Row? TryParseRow(string role, string line)
    {
        if (string.IsNullOrEmpty(role)) return null;
        if (!line.StartsWith('|')) return null;

        var cells = SplitCells(line);
        if (cells.Count < 7) return null;

        // Header and separator rows.
        if (cells[0].Equals("Privilege", StringComparison.OrdinalIgnoreCase)) return null;
        if (cells[0].Length > 0 && cells[0].All(c => c is '-' or ':')) return null;
        if (cells[0].Length == 0) return null;

        return new Row
        {
            Role = role,
            Privilege = cells[0],
            Access = cells[1] == "—" ? string.Empty : cells[1],
            Depth = cells[2],
            AppliesTo = cells[3],
            Justification = cells[4],
            FirstSeen = cells[5],
            Status = cells[6].Length == 0 ? "active" : cells[6]
        };
    }

    /// <summary>
    /// Splits a markdown table row, honouring the <c>\|</c> escaping <see cref="Escape"/> emits.
    /// Without that, a hand-written justification containing a pipe would add a cell and shift
    /// <c>First seen</c> and <c>Status</c> out of position.
    /// </summary>
    private static List<string> SplitCells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|') && !trimmed.EndsWith(@"\|")) trimmed = trimmed[..^1];

        var cells = new List<string>();
        var cell = new StringBuilder();

        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (c == '\\' && i + 1 < trimmed.Length && trimmed[i + 1] == '|')
            {
                cell.Append('|');
                i++;
                continue;
            }

            if (c == '|')
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
                continue;
            }

            cell.Append(c);
        }

        cells.Add(cell.ToString().Trim());
        return cells;
    }

    // ──────────────────────────────────────────────────────────
    // Merge
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Folds the live grant set into the existing rows. Existing justifications are carried over
    /// untouched; rows that no longer exist live are marked removed rather than deleted.
    /// </summary>
    public static MergeResult Merge(
        List<Row> existingRows,
        IReadOnlyList<RolePrivilegeGrantReader.RoleGrants> rolesInScope,
        IReadOnlyCollection<string> requestedRoleNames,
        string runDate)
    {
        var changes = new List<Change>();
        var byKey = existingRows
            .GroupBy(r => r.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var liveKeys = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<Row>(existingRows);

        foreach (var role in rolesInScope)
        {
            foreach (var live in Flatten(role))
            {
                if (!liveKeys.Add(live.Key)) continue;

                if (!byKey.TryGetValue(live.Key, out var existing))
                {
                    var added = live with { FirstSeen = runDate, Status = "active" };
                    merged.Add(added);
                    byKey[added.Key] = added;
                    changes.Add(new Change("new", added.Role, added.Privilege, added.Access,
                        $"{added.Depth} on {added.AppliesTo}"));
                    continue;
                }

                var updated = existing;

                if (!string.Equals(existing.Depth, live.Depth, StringComparison.OrdinalIgnoreCase))
                {
                    changes.Add(new Change("depth", existing.Role, existing.Privilege, existing.Access,
                        $"{existing.Depth} → {live.Depth}"));
                    updated = updated with { Depth = live.Depth };
                }

                if (!string.Equals(existing.AppliesTo, live.AppliesTo, StringComparison.Ordinal))
                {
                    changes.Add(new Change("scope", existing.Role, existing.Privilege, existing.Access,
                        $"{existing.AppliesTo} → {live.AppliesTo}"));
                    updated = updated with { AppliesTo = live.AppliesTo };
                }

                if (existing.IsRemoved)
                {
                    changes.Add(new Change("re-added", existing.Role, existing.Privilege, existing.Access, live.Depth));
                    updated = updated with { Status = "active" };
                }

                if (!ReferenceEquals(updated, existing))
                {
                    merged[merged.IndexOf(existing)] = updated;
                    byKey[updated.Key] = updated;
                }
            }
        }

        // Rows that were in the file but are no longer live — only for roles we actually read,
        // otherwise a narrowed --role filter would mark everything else removed.
        var readRoleNames = new HashSet<string>(rolesInScope.Select(r => r.RoleName), StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < merged.Count; i++)
        {
            var row = merged[i];
            if (row.IsRemoved) continue;
            if (!readRoleNames.Contains(row.Role)) continue;
            if (liveKeys.Contains(row.Key)) continue;

            merged[i] = row with { Status = $"removed {runDate}" };
            changes.Add(new Change("removed", row.Role, row.Privilege, row.Access, row.Depth));
        }

        var rolesNotFound = requestedRoleNames
            .Where(n => !readRoleNames.Contains(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MergeResult(merged, changes, rolesNotFound);
    }

    private static IEnumerable<Row> Flatten(RolePrivilegeGrantReader.RoleGrants role)
    {
        foreach (var grant in role.Grants)
        {
            yield return new Row
            {
                Role = role.RoleName,
                Privilege = grant.PrivilegeName,
                Access = grant.AccessRight == AccessRights.None ? string.Empty : ShortAccessName(grant.AccessRight),
                Depth = grant.Depth.ToString(),
                AppliesTo = FormatAppliesTo(grant.Entities),
                FirstSeen = string.Empty,
                Status = "active"
            };
        }
    }

    /// <summary>
    /// Names up to <see cref="AppliesToPreview"/> entities, then collapses to a count. The count is
    /// the auditable part — "31 entities" says the grant is org-wide in a way a truncated list does
    /// not — and a change in it is worth a change-log line.
    /// </summary>
    public static string FormatAppliesTo(IReadOnlyList<string> entities)
    {
        if (entities.Count == 0) return "_(no entity — task privilege)_";
        if (entities.Count <= AppliesToPreview) return string.Join(", ", entities);

        return $"{entities.Count} entities: "
            + string.Join(", ", entities.Take(AppliesToPreview))
            + ", …";
    }

    private static string ShortAccessName(AccessRights right)
    {
        var name = right.ToString();
        return name.EndsWith("Access", StringComparison.Ordinal) ? name[..^"Access".Length] : name;
    }

    // ──────────────────────────────────────────────────────────
    // Render
    // ──────────────────────────────────────────────────────────

    public static string Render(
        string environmentUrl,
        string? solutionName,
        string scope,
        string runStamp,
        List<Row> rows,
        List<Change> changes,
        List<string> rolesNotFound,
        string existingFreeText,
        string existingChangeLog)
    {
        var sb = new StringBuilder();
        var envLabel = EnvironmentLabel(environmentUrl);

        sb.AppendLine($"# Security role privilege audit — {envLabel}"
            + (string.IsNullOrWhiteSpace(solutionName) ? "" : $" / {solutionName}"));
        sb.AppendLine();

        // Deliberately tool-agnostic wording: this file is a deliverable that may live alongside
        // hand-written documentation, so it should not read as tooling output.
        sb.AppendLine(SummaryStart);
        sb.AppendLine("Every privilege currently granted to each role in scope, read from the environment. One row is one");
        sb.AppendLine("grant — the unit an administrator decides on — and **Applies to** lists the entities it reaches, so a");
        sb.AppendLine("privilege that covers every activity type in the org is one row rather than thirty.");
        sb.AppendLine();
        sb.AppendLine("Regeneration is append-only: it never deletes a row and never writes the **Justification** column —");
        sb.AppendLine("that column is maintained by hand and is the point of the document. It may hold prose or a reference");
        sb.AppendLine("to a reason recorded in the privilege log (`J-nnn`). A privilege that disappears from the environment");
        sb.AppendLine("is marked `removed <date>` and kept, because a withdrawn grant is part of the audit trail. Text");
        sb.AppendLine("between this block and the change log is preserved across runs, so notes can be added there.");
        sb.AppendLine();

        var active = rows.Count(r => !r.IsRemoved);
        var removed = rows.Count - active;
        var unjustified = rows.Count(r => !r.IsRemoved && !r.IsJustified);
        var roleCount = rows.Select(r => r.Role).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        sb.AppendLine($"- **Environment:** {environmentUrl}");
        if (!string.IsNullOrWhiteSpace(solutionName)) sb.AppendLine($"- **Solution:** {solutionName}");
        sb.AppendLine($"- **Scope:** {scope}");
        sb.AppendLine($"- **Last run:** {runStamp}");
        sb.AppendLine($"- **Roles:** {roleCount}");
        sb.AppendLine($"- **Grants tracked:** {rows.Count} ({active} active, {removed} removed)");
        sb.AppendLine($"- **Awaiting justification:** {unjustified}");
        if (rolesNotFound.Count > 0)
            sb.AppendLine($"- **Roles in scope but not found in the environment:** {string.Join(", ", rolesNotFound)}");
        sb.AppendLine(SummaryEnd);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(existingFreeText))
        {
            sb.AppendLine(existingFreeText.Trim('\n'));
            sb.AppendLine();
        }

        sb.AppendLine(ChangeLogHeading);
        sb.AppendLine();
        AppendChangeLogEntry(sb, runStamp, envLabel, rows, changes, rolesNotFound);
        if (!string.IsNullOrWhiteSpace(existingChangeLog))
        {
            sb.AppendLine(existingChangeLog.Trim('\n'));
            sb.AppendLine();
        }

        sb.AppendLine(PrivilegesHeading);
        sb.AppendLine();

        foreach (var roleGroup in rows
            .GroupBy(r => r.Role, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"### {roleGroup.Key}");
            sb.AppendLine();
            sb.AppendLine("| Privilege | Access | Depth | Applies to | Justification | First seen | Status |");
            sb.AppendLine("|---|---|---|---|---|---|---|");

            foreach (var row in roleGroup
                .OrderBy(r => r.AppliesTo, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => AccessSortKey(r.Access))
                .ThenBy(r => r.Privilege, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"| {row.Privilege} | {Dash(row.Access)} | {row.Depth} | {row.AppliesTo} "
                    + $"| {Escape(row.Justification)} | {row.FirstSeen} | {row.Status} |");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendChangeLogEntry(
        StringBuilder sb,
        string runStamp,
        string envLabel,
        List<Row> rows,
        List<Change> changes,
        List<string> rolesNotFound)
    {
        var counts = changes.GroupBy(c => c.Kind).ToDictionary(g => g.Key, g => g.Count());
        var newCount = counts.GetValueOrDefault("new");
        var isBaseline = newCount == rows.Count && changes.Count == newCount;

        sb.AppendLine($"### {runStamp} — {envLabel}");
        sb.AppendLine();

        if (changes.Count == 0 && rolesNotFound.Count == 0)
        {
            sb.AppendLine("No changes.");
            sb.AppendLine();
            return;
        }

        var summary = new List<string>();
        foreach (var kind in new[] { "new", "depth", "scope", "removed", "re-added" })
        {
            var n = counts.GetValueOrDefault(kind);
            if (n > 0) summary.Add($"{n} {kind}");
        }
        sb.AppendLine(summary.Count > 0 ? string.Join(", ", summary) + "." : "No privilege changes.");
        sb.AppendLine();

        foreach (var role in rolesNotFound)
            sb.AppendLine($"- Role **{role}** is in scope but does not exist in this environment; its rows were left untouched.");
        if (rolesNotFound.Count > 0) sb.AppendLine();

        if (isBaseline)
        {
            sb.AppendLine($"Initial baseline — all {rows.Count} grants recorded for the first time. "
                + "Individual rows are not listed here; see the privilege tables below.");
            sb.AppendLine();
            return;
        }

        // Nothing changed privilege-wise — the entry above (e.g. a role-not-found note) is the
        // whole story, so don't emit an empty detail table.
        if (changes.Count == 0) return;

        const int detailLimit = 200;
        var detailed = changes.Take(detailLimit).ToList();

        sb.AppendLine("| Change | Role | Privilege | Access | Detail |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var c in detailed)
            sb.AppendLine($"| {c.Kind} | {c.Role} | {c.Privilege} | {Dash(c.Access)} | {Escape(c.Detail)} |");
        sb.AppendLine();

        if (changes.Count > detailed.Count)
        {
            sb.AppendLine($"_{changes.Count - detailed.Count} further changes not listed individually "
                + $"(detail is capped at {detailLimit} rows per run); the privilege tables below carry all of them._");
            sb.AppendLine();
        }
    }

    private static int AccessSortKey(string access)
    {
        var index = Array.FindIndex(AccessOrder, p => p.Equals(access, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? AccessOrder.Length : index;
    }

    private static string Dash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string Escape(string value) => value.Replace("|", "\\|");

    private static string EnvironmentLabel(string environmentUrl)
    {
        if (string.IsNullOrWhiteSpace(environmentUrl)) return "unknown environment";
        if (!Uri.TryCreate(environmentUrl, UriKind.Absolute, out var uri)) return environmentUrl;
        var host = uri.Host;
        var dot = host.IndexOf('.');
        return dot > 0 ? host[..dot] : host;
    }
}
