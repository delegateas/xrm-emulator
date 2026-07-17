using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using XrmEmulator.MetadataSync.Models;
using XrmEmulator.MetadataSync.Readers;

namespace XrmEmulator.MetadataSync.Writers;

/// <summary>
/// Reads/merges/applies the org-wide orgdborgsettings XML blob (a single column on the
/// singleton organization record holding many unrelated settings as sibling elements).
///
/// The merge/parse helpers here are shared by the live `orgsetting set` staging command
/// (preview only, not persisted) and the commit-time apply below (always recomputed against
/// a freshly re-fetched live blob, never the possibly-stale blob captured at staging time).
/// </summary>
public static class OrgDbOrgSettingsWriter
{
    /// <summary>
    /// Merges a single named setting into the blob, leaving every sibling element untouched.
    /// Refuses to touch a blob whose root element isn't &lt;OrgSettings&gt; — this column is
    /// undocumented and shared by many unrelated tools, so we only ever operate on the one
    /// documented shape rather than guessing at anything else.
    /// </summary>
    public static string MergeSetting(string? currentXml, string settingName, string newValue)
    {
        var doc = string.IsNullOrWhiteSpace(currentXml)
            ? new XDocument(new XElement("OrgSettings"))
            : XDocument.Parse(currentXml, LoadOptions.PreserveWhitespace);

        var root = doc.Root ?? throw new InvalidOperationException("orgdborgsettings XML has no root element.");
        if (!root.Name.LocalName.Equals("OrgSettings", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"orgdborgsettings root element is '{root.Name.LocalName}', expected 'OrgSettings' — " +
                "refusing to modify a structure that doesn't match the known shape.");
        }

        var existing = root.Elements().FirstOrDefault(e =>
            e.Name.LocalName.Equals(settingName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            existing.Value = newValue;
        else
            root.Add(new XElement(settingName, newValue));

        return doc.ToString(SaveOptions.DisableFormatting);
    }

    public static string? GetSettingValue(string? xml, string settingName)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        var root = XDocument.Parse(xml).Root;
        return root?.Elements().FirstOrDefault(e =>
            e.Name.LocalName.Equals(settingName, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    public static List<(string Name, string Value)> ParseAll(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return [];
        var root = XDocument.Parse(xml).Root;
        return root?.Elements().Select(e => (e.Name.LocalName, e.Value)).ToList() ?? [];
    }

    /// <summary>
    /// Commit-time apply for Mode = SetValue. Always re-fetches the live blob rather than
    /// trusting the definition's BaselineXml, so a concurrent unrelated org-setting change
    /// (made by someone else between staging and commit) is never silently clobbered.
    /// </summary>
    public static void ApplySetValue(IOrganizationService service, OrgDbOrgSettingDefinition def, Action<string>? log = null)
    {
        var (liveOrgId, liveXml) = OrgDbOrgSettingsReader.RetrieveLive(service);
        if (liveOrgId != def.OrganizationId)
            throw new InvalidOperationException("Organization id does not match what was captured at staging time.");

        var currentTargetValue = GetSettingValue(liveXml, def.SettingName!);

        if (string.Equals(liveXml, def.BaselineXml, StringComparison.Ordinal))
        {
            // Clean case: nothing has changed since staging — safe to merge and write.
            var newXml = MergeSetting(liveXml, def.SettingName!, def.NewValue!);
            service.Update(new Entity("organization", liveOrgId) { ["orgdborgsettings"] = newXml });
        }
        else if (string.Equals(currentTargetValue, def.NewValue, StringComparison.OrdinalIgnoreCase))
        {
            // Self-heal: the target value is already live, most likely because an earlier
            // attempt of this same commit item actually wrote it but the read-after-write
            // check below failed transiently (e.g. replica lag). Skip re-writing; just re-verify.
            log?.Invoke("  Target value already live (a prior attempt likely succeeded) — skipping duplicate write.");
        }
        else
        {
            // Genuinely different from both our baseline and our target: someone/something
            // else changed orgdborgsettings concurrently. Abort loudly rather than clobber it.
            throw new InvalidOperationException(
                $"orgdborgsettings has changed since 'orgsetting set {def.SettingName} {def.NewValue}' was staged " +
                "(live content differs from both the captured baseline and the intended result). Aborting to avoid " +
                $"overwriting a concurrent change. Re-run 'orgsetting set {def.SettingName} {def.NewValue}' to " +
                "restage against the current value.");
        }

        // Read-after-write verification — Dataverse can silently ignore unknown setting names.
        var (_, verifyXml) = OrgDbOrgSettingsReader.RetrieveLive(service);
        var verifyValue = GetSettingValue(verifyXml, def.SettingName!);
        if (!string.Equals(verifyValue, def.NewValue, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Verification failed: after Update, '{def.SettingName}' reads back as " +
                $"'{verifyValue ?? "(absent)"}' instead of '{def.NewValue}'. Dataverse may have rejected this " +
                "setting name. Item left in _pending/ for investigation/retry — nothing was archived.");
        }

        log?.Invoke($"  Verified live: '{def.SettingName}' = '{def.NewValue}'.");
    }

    /// <summary>
    /// Commit-time apply for Mode = RestoreBlob (rollback). No freshness/concurrency gate —
    /// this is the corrective path and must not itself be blockable by drift.
    /// </summary>
    public static void ApplyRestoreBlob(IOrganizationService service, OrgDbOrgSettingDefinition def, Action<string>? log = null)
    {
        var (liveOrgId, _) = OrgDbOrgSettingsReader.RetrieveLive(service);
        service.Update(new Entity("organization", liveOrgId) { ["orgdborgsettings"] = def.RestoreXml });

        var (_, verifyXml) = OrgDbOrgSettingsReader.RetrieveLive(service);
        if (!ElementsEqual(ParseAll(verifyXml), ParseAll(def.RestoreXml)))
        {
            throw new InvalidOperationException(
                "Rollback verification failed: live orgdborgsettings does not match the restored blob after Update.");
        }

        log?.Invoke("  Rollback verified: live orgdborgsettings matches the backed-up pre-change blob.");
    }

    private static bool ElementsEqual(List<(string Name, string Value)> a, List<(string Name, string Value)> b)
    {
        if (a.Count != b.Count) return false;
        var aSorted = a.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var bSorted = b.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < aSorted.Count; i++)
        {
            if (!aSorted[i].Name.Equals(bSorted[i].Name, StringComparison.OrdinalIgnoreCase)) return false;
            if (!aSorted[i].Value.Equals(bSorted[i].Value, StringComparison.Ordinal)) return false;
        }
        return true;
    }
}
