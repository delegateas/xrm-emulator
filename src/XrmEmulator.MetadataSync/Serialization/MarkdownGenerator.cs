using System.Text;
using DG.Tools.XrmMockup;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Serialization;

public static class MarkdownGenerator
{
    public static void Generate(
        SyncOptions options,
        Dictionary<string, EntityMetadata> entityMetadata,
        Dictionary<string, Dictionary<int, int>> defaultStateStatus,
        List<MetaPlugin>? plugins,
        OptionSetMetadataBase[]? optionSets,
        List<SecurityRole>? securityRoles)
    {
        var outputDir = Path.GetFullPath(options.OutputDirectory);
        var modelDir = Path.Combine(outputDir, "Model");
        var entitiesDir = Path.Combine(modelDir, "entities");
        Directory.CreateDirectory(entitiesDir);

        GenerateSolutionOverview(entityMetadata, modelDir, options.SolutionUniqueName);

        foreach (var (logicalName, metadata) in entityMetadata)
        {
            GenerateEntityFile(metadata, defaultStateStatus, entitiesDir);
        }

        GenerateGlobalOptionSets(optionSets, modelDir);
        GeneratePlugins(plugins, modelDir);
        GenerateSecurityRoles(securityRoles, modelDir);
    }

    private static string GetLabel(Label? label) =>
        label?.UserLocalizedLabel?.Label ?? "";

    private static void GenerateSolutionOverview(
        Dictionary<string, EntityMetadata> entityMetadata,
        string modelDir,
        string solutionUniqueName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Solution Overview");
        sb.AppendLine();
        sb.AppendLine($"**Solution Unique Name:** `{solutionUniqueName}`");
        sb.AppendLine();
        sb.AppendLine("| Logical Name | Display Name | Ownership | Custom |");
        sb.AppendLine("|---|---|---|---|");

        foreach (var (logicalName, meta) in entityMetadata.OrderBy(e => e.Key))
        {
            var displayName = GetLabel(meta.DisplayName);
            var ownership = meta.OwnershipType?.ToString() ?? "";
            var isCustom = meta.IsCustomEntity == true ? "Yes" : "No";
            sb.AppendLine($"| [{logicalName}](entities/{logicalName}.md) | {displayName} | {ownership} | {isCustom} |");
        }

        File.WriteAllText(Path.Combine(modelDir, "solution.md"), sb.ToString());
    }

    private static void GenerateEntityFile(
        EntityMetadata metadata,
        Dictionary<string, Dictionary<int, int>> defaultStateStatus,
        string entitiesDir)
    {
        var logicalName = metadata.LogicalName;
        var displayName = GetLabel(metadata.DisplayName);
        var sb = new StringBuilder();

        sb.AppendLine($"# {displayName} ({logicalName})");
        sb.AppendLine();
        sb.AppendLine($"- **Primary ID:** {metadata.PrimaryIdAttribute}");
        sb.AppendLine($"- **Primary Name:** {metadata.PrimaryNameAttribute}");
        sb.AppendLine($"- **Ownership:** {metadata.OwnershipType}");
        sb.AppendLine($"- **Is Custom:** {(metadata.IsCustomEntity == true ? "Yes" : "No")}");
        sb.AppendLine();

        // Columns
        if (metadata.Attributes is { Length: > 0 })
        {
            sb.AppendLine("## Columns");
            sb.AppendLine();
            sb.AppendLine("| Logical Name | Display Name | Type | Required | Description |");
            sb.AppendLine("|---|---|---|---|---|");

            foreach (var attr in metadata.Attributes.OrderBy(a => a.LogicalName))
            {
                var attrDisplay = GetLabel(attr.DisplayName);
                var typeDesc = GetTypeDescription(attr);
                var required = GetRequiredLevel(attr);
                var description = EscapePipe(GetLabel(attr.Description));
                sb.AppendLine($"| {attr.LogicalName} | {attrDisplay} | {typeDesc} | {required} | {description} |");
            }

            sb.AppendLine();

            // Option set columns
            var optionSetAttrs = metadata.Attributes
                .Where(a => a is EnumAttributeMetadata { OptionSet.Options.Count: > 0 })
                .OrderBy(a => a.LogicalName)
                .ToList();

            if (optionSetAttrs.Count > 0)
            {
                sb.AppendLine("## Option Set Columns");
                sb.AppendLine();

                foreach (var attr in optionSetAttrs)
                {
                    var enumAttr = (EnumAttributeMetadata)attr;
                    var attrDisplay = GetLabel(attr.DisplayName);
                    sb.AppendLine($"### {attr.LogicalName} - {attrDisplay}");
                    sb.AppendLine();

                    var isStatus = attr is StatusAttributeMetadata;
                    if (isStatus)
                    {
                        sb.AppendLine("| Value | Label | State |");
                        sb.AppendLine("|---|---|---|");
                        foreach (var opt in enumAttr.OptionSet.Options.OrderBy(o => o.Value))
                        {
                            var optLabel = GetLabel(opt.Label);
                            var state = opt is StatusOptionMetadata som ? som.State?.ToString() ?? "" : "";
                            sb.AppendLine($"| {opt.Value} | {optLabel} | {state} |");
                        }
                    }
                    else
                    {
                        sb.AppendLine("| Value | Label |");
                        sb.AppendLine("|---|---|");
                        foreach (var opt in enumAttr.OptionSet.Options.OrderBy(o => o.Value))
                        {
                            var optLabel = GetLabel(opt.Label);
                            sb.AppendLine($"| {opt.Value} | {optLabel} |");
                        }
                    }

                    sb.AppendLine();
                }
            }
        }

        // Relationships
        var hasOneToMany = metadata.OneToManyRelationships is { Length: > 0 };
        var hasManyToOne = metadata.ManyToOneRelationships is { Length: > 0 };
        var hasManyToMany = metadata.ManyToManyRelationships is { Length: > 0 };

        if (hasOneToMany || hasManyToOne || hasManyToMany)
        {
            sb.AppendLine("## Relationships");
            sb.AppendLine();

            if (hasOneToMany)
            {
                sb.AppendLine("### One-to-Many");
                sb.AppendLine();
                sb.AppendLine("| Schema Name | Related Entity | Lookup Attribute |");
                sb.AppendLine("|---|---|---|");
                foreach (var rel in metadata.OneToManyRelationships!.OrderBy(r => r.SchemaName))
                {
                    sb.AppendLine($"| {rel.SchemaName} | {rel.ReferencingEntity} | {rel.ReferencingAttribute} |");
                }
                sb.AppendLine();
            }

            if (hasManyToOne)
            {
                sb.AppendLine("### Many-to-One");
                sb.AppendLine();
                sb.AppendLine("| Schema Name | Referenced Entity | Lookup Attribute |");
                sb.AppendLine("|---|---|---|");
                foreach (var rel in metadata.ManyToOneRelationships!.OrderBy(r => r.SchemaName))
                {
                    sb.AppendLine($"| {rel.SchemaName} | {rel.ReferencedEntity} | {rel.ReferencingAttribute} |");
                }
                sb.AppendLine();
            }

            if (hasManyToMany)
            {
                sb.AppendLine("### Many-to-Many");
                sb.AppendLine();
                sb.AppendLine("| Schema Name | Related Entity | Intersect Entity |");
                sb.AppendLine("|---|---|---|");
                foreach (var rel in metadata.ManyToManyRelationships!.OrderBy(r => r.SchemaName))
                {
                    var relatedEntity = rel.Entity1LogicalName == logicalName
                        ? rel.Entity2LogicalName
                        : rel.Entity1LogicalName;
                    sb.AppendLine($"| {rel.SchemaName} | {relatedEntity} | {rel.IntersectEntityName} |");
                }
                sb.AppendLine();
            }
        }

        File.WriteAllText(Path.Combine(entitiesDir, $"{logicalName}.md"), sb.ToString());
    }

    private static string GetTypeDescription(AttributeMetadata attr)
    {
        return attr switch
        {
            StringAttributeMetadata s => $"String (max: {s.MaxLength})",
            MemoAttributeMetadata m => $"Memo (max: {m.MaxLength})",
            IntegerAttributeMetadata i => $"Integer (min: {i.MinValue}, max: {i.MaxValue})",
            DecimalAttributeMetadata d => $"Decimal (precision: {d.Precision})",
            DoubleAttributeMetadata db => $"Double (precision: {db.Precision})",
            MoneyAttributeMetadata mo => $"Money (precision: {mo.Precision})",
            DateTimeAttributeMetadata dt => $"DateTime ({dt.Format})",
            LookupAttributeMetadata lk => $"Lookup → {string.Join(", ", lk.Targets ?? [])}",
            BooleanAttributeMetadata b => FormatBooleanType(b),
            StatusAttributeMetadata => "Status",
            StateAttributeMetadata => "State",
            PicklistAttributeMetadata => "OptionSet",
            MultiSelectPicklistAttributeMetadata => "MultiSelect OptionSet",
            BigIntAttributeMetadata => "BigInt",
            UniqueIdentifierAttributeMetadata => "UniqueIdentifier",
            ImageAttributeMetadata => "Image",
            FileAttributeMetadata => "File",
            EntityNameAttributeMetadata => "EntityName",
            ManagedPropertyAttributeMetadata => "ManagedProperty",
            _ => attr.AttributeType?.ToString() ?? "Unknown"
        };
    }

    private static string FormatBooleanType(BooleanAttributeMetadata b)
    {
        var trueLabel = GetLabel(b.OptionSet?.TrueOption?.Label);
        var falseLabel = GetLabel(b.OptionSet?.FalseOption?.Label);
        if (string.IsNullOrEmpty(trueLabel) && string.IsNullOrEmpty(falseLabel))
            return "Boolean";
        return $"Boolean (true={trueLabel}, false={falseLabel})";
    }

    private static string GetRequiredLevel(AttributeMetadata attr)
    {
        return attr.RequiredLevel?.Value switch
        {
            AttributeRequiredLevel.ApplicationRequired => "Required",
            AttributeRequiredLevel.SystemRequired => "System",
            AttributeRequiredLevel.Recommended => "Recommended",
            _ => "Optional"
        };
    }

    private static string EscapePipe(string text) =>
        text.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");

    private static void GenerateGlobalOptionSets(
        OptionSetMetadataBase[]? optionSets,
        string modelDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Global Option Sets");
        sb.AppendLine();

        if (optionSets is not { Length: > 0 })
        {
            sb.AppendLine("No global option sets found.");
            File.WriteAllText(Path.Combine(modelDir, "global-optionsets.md"), sb.ToString());
            return;
        }

        foreach (var os in optionSets.OrderBy(o => o.Name))
        {
            var displayName = GetLabel(os.DisplayName);
            sb.AppendLine($"## {os.Name} - {displayName}");
            sb.AppendLine();

            if (os is OptionSetMetadata osm && osm.Options.Count > 0)
            {
                sb.AppendLine("| Value | Label |");
                sb.AppendLine("|---|---|");
                foreach (var opt in osm.Options.OrderBy(o => o.Value))
                {
                    sb.AppendLine($"| {opt.Value} | {GetLabel(opt.Label)} |");
                }
            }
            else if (os is BooleanOptionSetMetadata bos)
            {
                sb.AppendLine("| Value | Label |");
                sb.AppendLine("|---|---|");
                sb.AppendLine($"| True | {GetLabel(bos.TrueOption?.Label)} |");
                sb.AppendLine($"| False | {GetLabel(bos.FalseOption?.Label)} |");
            }
            else
            {
                sb.AppendLine("No options defined.");
            }

            sb.AppendLine();
        }

        File.WriteAllText(Path.Combine(modelDir, "global-optionsets.md"), sb.ToString());
    }

    private static void GeneratePlugins(
        List<MetaPlugin>? plugins,
        string modelDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Plugin Registrations");
        sb.AppendLine();

        if (plugins is not { Count: > 0 })
        {
            sb.AppendLine("No plugin registrations found.");
            File.WriteAllText(Path.Combine(modelDir, "plugins.md"), sb.ToString());
            return;
        }

        var grouped = plugins
            .GroupBy(p => p.PrimaryEntity ?? "none")
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();
            sb.AppendLine("| Name | Message | Stage | Mode | Rank | Assembly |");
            sb.AppendLine("|---|---|---|---|---|---|");

            foreach (var plugin in group.OrderBy(p => p.Stage).ThenBy(p => p.Rank))
            {
                var stage = plugin.Stage switch
                {
                    10 => "PreValidation",
                    20 => "PreOperation",
                    40 => "PostOperation",
                    _ => plugin.Stage.ToString()
                };
                var mode = plugin.Mode == 0 ? "Sync" : "Async";
                sb.AppendLine($"| {plugin.Name} | {plugin.MessageName} | {stage} | {mode} | {plugin.Rank} | {plugin.AssemblyName} |");
            }

            sb.AppendLine();
        }

        File.WriteAllText(Path.Combine(modelDir, "plugins.md"), sb.ToString());
    }

    private static void GenerateSecurityRoles(
        List<SecurityRole>? securityRoles,
        string modelDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Security Roles");
        sb.AppendLine();

        if (securityRoles is not { Count: > 0 })
        {
            sb.AppendLine("No security roles found.");
            File.WriteAllText(Path.Combine(modelDir, "security-roles.md"), sb.ToString());
            return;
        }

        sb.AppendLine("| Name | Role ID | Privileges Count |");
        sb.AppendLine("|---|---|---|");

        foreach (var role in securityRoles.OrderBy(r => r.Name))
        {
            var privCount = role.Privileges?.Count ?? 0;
            sb.AppendLine($"| {role.Name} | {role.RoleId} | {privCount} |");
        }

        sb.AppendLine();

        // Detailed sections per role
        foreach (var role in securityRoles.OrderBy(r => r.Name))
        {
            sb.AppendLine($"## {role.Name}");
            sb.AppendLine();
            sb.AppendLine($"- **Role ID:** {role.RoleId}");
            sb.AppendLine($"- **Role Template ID:** {role.RoleTemplateId}");
            sb.AppendLine();

            if (role.Privileges is { Count: > 0 })
            {
                sb.AppendLine("| Privilege | Access Rights | Depth |");
                sb.AppendLine("|---|---|---|");

                foreach (var (privName, rights) in role.Privileges.OrderBy(p => p.Key))
                {
                    foreach (var (accessRight, rolPriv) in rights.OrderBy(r => r.Key))
                    {
                        sb.AppendLine($"| {privName} | {accessRight} | {rolPriv.PrivilegeDepth} |");
                    }
                }

                sb.AppendLine();
            }
        }

        File.WriteAllText(Path.Combine(modelDir, "security-roles.md"), sb.ToString());
    }
}
