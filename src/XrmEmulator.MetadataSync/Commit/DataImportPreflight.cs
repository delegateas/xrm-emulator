using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Commit;

/// <summary>
/// Resolves every <c>lookup:&lt;table&gt;:&lt;field&gt;</c> value in the selected data imports against the
/// target environment <em>before</em> the first row is written.
///
/// Without it, such a reference is only tested when its own row is reached: the import writes the rows
/// before it, stops on the missing one, and the operator fixes that single record and re-runs — meeting
/// the next missing one after the next few hundred rows. The answer lives in the target environment, so
/// no amount of checking where the files are produced can find it; asking the target up front can, and
/// the whole list is worth more than the first item on it.
/// </summary>
public static class DataImportPreflight
{
    /// <param name="Rows">How many rows in the file carry this value — the real cost of the miss.</param>
    /// <param name="Skippable">
    /// The file allows rows with unresolvable lookups to be skipped, so this one costs rows, not the run.
    /// </param>
    public sealed record Miss(
        string File,
        string Table,
        string Field,
        string Value,
        int Rows,
        bool Skippable,
        bool Ambiguous);

    /// <summary>
    /// Values are checked in batches per (table, field) rather than one query per row: a file with 1300
    /// rows would otherwise open 1300 queries to answer a question about a few dozen distinct values.
    /// </summary>
    private const int BatchSize = 250;

    public static List<Miss> Run(
        IOrganizationService service,
        IEnumerable<CommitItem> items,
        string pendingDir,
        Action<string>? log)
    {
        var misses = new List<Miss>();

        foreach (var item in items)
        {
            if (item.Type != CommitItemType.DataImport || item.ParsedData is not DataImportDefinition def)
                continue;

            var lookupFields = (def.FieldTypes ?? [])
                .Select(kvp => (Field: kvp.Key, Parts: kvp.Value.Split(':')))
                .Where(x => x.Parts.Length == 3
                            && x.Parts[0].Equals("lookup", StringComparison.OrdinalIgnoreCase))
                .Select(x => (Field: x.Field, Table: x.Parts[1], MatchField: x.Parts[2]))
                .ToList();
            if (lookupFields.Count == 0)
                continue;

            var fileName = Path.GetRelativePath(pendingDir, item.FilePath).Replace('\\', '/');
            var skippable = def.SkipRowsWithUnresolvedLookups == true;

            foreach (var (field, table, matchField) in lookupFields)
            {
                // Row counts per distinct value: one missing user that covers 12 rows is a different
                // problem from one that covers 1, and the operator has to be able to tell them apart.
                var rowsByValue = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in def.Rows)
                {
                    if (!row.TryGetValue(field, out var raw) || raw is null
                        || raw.Value.ValueKind == JsonValueKind.Null)
                        continue;
                    var value = raw.Value.ValueKind == JsonValueKind.String
                        ? raw.Value.GetString() ?? ""
                        : raw.Value.ToString();
                    value = value.Trim();
                    // A "{{_pending/…}}" reference is answered by an earlier file in this same run, not
                    // by the target, so there is nothing to look up yet.
                    if (value.Length == 0 || value.Contains("{{", StringComparison.Ordinal))
                        continue;
                    rowsByValue[value] = rowsByValue.GetValueOrDefault(value) + 1;
                }
                if (rowsByValue.Count == 0)
                    continue;

                var counts = CountMatches(service, table, matchField, rowsByValue.Keys.ToList());
                foreach (var (value, rowCount) in rowsByValue)
                {
                    var found = counts.GetValueOrDefault(value);
                    if (found == 1)
                        continue;
                    misses.Add(new Miss(fileName, table, matchField, value, rowCount,
                        // An ambiguous lookup is never skippable, whatever the file says: two candidate
                        // records mean the target data is wrong, and picking one is not a smaller
                        // decision than stopping.
                        Skippable: skippable && found == 0,
                        Ambiguous: found > 1));
                }
            }
        }

        if (misses.Count > 0)
            Report(misses, log);

        return misses;
    }

    /// <summary>
    /// How many target records carry each value. Returns 0 for a value with no record at all — the
    /// difference between "none" and "several" is what decides whether a row may be skipped.
    /// </summary>
    private static Dictionary<string, int> CountMatches(
        IOrganizationService service, string table, string matchField, List<string> values)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
            counts[value] = 0;

        foreach (var batch in Chunk(values, BatchSize))
        {
            var query = new QueryExpression(table)
            {
                ColumnSet = new ColumnSet(matchField),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(matchField, ConditionOperator.In, batch.Cast<object>().ToArray())
                    }
                }
            };

            // Paged: a batch of 250 values can legitimately return more than the default page size when
            // some of them are duplicated in the target, which is exactly the case being detected.
            query.PageInfo = new PagingInfo { Count = 500, PageNumber = 1 };
            while (true)
            {
                var page = service.RetrieveMultiple(query);
                foreach (var e in page.Entities)
                {
                    var actual = e.GetAttributeValue<string>(matchField);
                    if (actual != null && counts.ContainsKey(actual))
                        counts[actual]++;
                }
                if (!page.MoreRecords) break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
        }

        return counts;
    }

    private static IEnumerable<List<string>> Chunk(List<string> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
    }

    private static void Report(List<Miss> misses, Action<string>? log)
    {
        var blocking = misses.Where(m => !m.Skippable).ToList();
        var skippable = misses.Where(m => m.Skippable).ToList();

        log?.Invoke($"Preflight: {misses.Count} reference(s) in the selected files do not resolve to exactly "
                    + "one record in the target environment.");

        foreach (var group in misses.GroupBy(m => m.File).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var rows = group.Sum(m => m.Rows);
            log?.Invoke($"  {group.Key}: {group.Count()} value(s) across {rows} row(s)");
            foreach (var m in group.OrderByDescending(m => m.Rows).ThenBy(m => m.Value, StringComparer.Ordinal))
            {
                var what = m.Ambiguous
                    ? $"more than one {m.Table} has {m.Field} = '{m.Value}'"
                    : $"no {m.Table} where {m.Field} = '{m.Value}'";
                var effect = m.Skippable ? "row(s) will be skipped" : "row(s) blocked";
                log?.Invoke($"    {what} — {m.Rows} {effect}");
            }
        }

        if (skippable.Count > 0)
            log?.Invoke($"  {skippable.Sum(m => m.Rows)} row(s) will be skipped and listed again as they are "
                        + "reached. Create the missing records and run those files again to add them.");
        if (blocking.Count > 0)
            log?.Invoke($"  {blocking.Sum(m => m.Rows)} row(s) cannot be written and would stop their file.");
    }

    /// <summary>
    /// The operator-facing message for the misses that no file allows to be skipped. Deliberately says
    /// what has <em>not</em> happened yet: the preflight runs before the first write, so the run can be
    /// repeated from scratch once the target has been corrected.
    /// </summary>
    public static string BuildBlockingMessage(List<Miss> misses)
    {
        var blocking = misses.Where(m => !m.Skippable).ToList();
        var lines = blocking
            .GroupBy(m => m.File)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"  {g.Key}: {g.Count()} value(s), {g.Sum(m => m.Rows)} row(s)\n"
                         + string.Join("\n", g
                             .OrderByDescending(m => m.Rows)
                             .Select(m => m.Ambiguous
                                 ? $"    more than one {m.Table} has {m.Field} = '{m.Value}' ({m.Rows} row(s))"
                                 : $"    no {m.Table} where {m.Field} = '{m.Value}' ({m.Rows} row(s))")));

        return "Preflight stopped the commit: the target environment is missing records that the selected "
               + "files point at. Nothing has been written.\n"
               + string.Join("\n", lines)
               + "\n\nCreate the records listed above in the target environment and run the commit again, or "
               + "de-select the file(s) they are in and run the rest now — every file is upserted on its own "
               + "match key, so the missing one can be run on its own later.";
    }
}
