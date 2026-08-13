using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using DG.Tools.XrmMockup;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

namespace XrmEmulator.Services;

/// <summary>
/// Builds a combined XrmMockup metadata folder from multiple solution export directories.
/// Each solution export directory is expected to contain a Metadata.xml file, and optionally
/// SecurityRoles/ and Workflows/ subdirectories.
/// </summary>
public static class MetadataFolderBuilder
{
    /// <summary>
    /// Scans the solution exports root path for Metadata.xml files across all solution directories,
    /// merges them into a single combined metadata folder that XrmMockup can load.
    /// Returns the path to the combined folder.
    /// </summary>
    /// <param name="solutionExportsPath">Root path containing per-solution export directories.</param>
    /// <param name="excludedPluginTypeNames">
    /// Fully-qualified plugin type names to drop from the merged metadata before XrmMockup loads it.
    /// Use this for plugins that query Dataverse system tables XrmMockup does not model (e.g.
    /// "privilege"/"roleprivileges") and would otherwise throw "No EntityMetadata found" on execution.
    /// Excluded here at metadata-merge time only — the real CRM registration is untouched.
    /// </param>
    /// <param name="includeRolePrivileges">
    /// When false, security roles are copied with their privilege lists emptied. XrmMockup only
    /// enforces access for entities some role mentions, so stripping privileges turns role-based
    /// authorisation off wholesale while keeping every role present by name and id. Use it for
    /// suites written before the export carried real privileges; leave it true everywhere else.
    /// </param>
    public static string BuildCombinedMetadataFolder(
        string solutionExportsPath,
        IEnumerable<string>? excludedPluginTypeNames = null,
        bool includeRolePrivileges = true)
    {
        var outputDir = Path.Combine(Path.GetTempPath(), "xrm-emulator-metadata", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(outputDir);

        var securityRolesDir = Path.Combine(outputDir, "SecurityRoles");
        var workflowsDir = Path.Combine(outputDir, "Workflows");
        Directory.CreateDirectory(securityRolesDir);
        Directory.CreateDirectory(workflowsDir);

        // Find all solution export directories (each contains Metadata.xml)
        var metadataFiles = FindMetadataFiles(solutionExportsPath);

        if (metadataFiles.Count == 0)
            throw new InvalidOperationException(
                $"No Metadata.xml files found under '{solutionExportsPath}'. " +
                "Run MetadataSync to export solution metadata first.");

        // Deserialize and merge all MetadataSkeleton files
        var serializer = new DataContractSerializer(typeof(MetadataSkeleton));
        MetadataSkeleton? combined = null;

        // Track seen security role IDs across all solutions to avoid duplicates.
        // The same role GUID can appear in multiple solutions (e.g. shared system roles),
        // and XrmMockup's Security ctor does ToDictionary(s => s.RoleId) which throws on dupes.
        var seenRoleIds = new HashSet<Guid>();

        // Track seen workflow IDs across all solutions and across both emission paths
        // (plain Workflows/*.xml copy + SolutionExport xaml conversion). The same workflow
        // GUID can appear in multiple solutions (e.g. a shared lead workflow committed to
        // both PartnerHierarki and KFSales); XrmMockup's Core adds each workflow entity to
        // its DB and throws on a duplicate id.
        var seenWorkflowIds = new HashSet<Guid>();

        // MetadataSkeleton.Merge only merges EntityMetadata and DefaultStateStatus — plugin steps
        // from every solution after the first are dropped. Collect them here instead, so a step
        // registered in one solution still fires when another solution is loaded first.
        var mergedPlugins = new List<MetaPlugin>();
        var seenPluginKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var metadataFile in metadataFiles)
        {
            MetadataSkeleton skeleton;
            using (var stream = new FileStream(metadataFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                skeleton = (MetadataSkeleton)serializer.ReadObject(stream)!;
            }

            if (combined == null)
            {
                combined = skeleton;
            }
            else
            {
                combined.Merge(skeleton);
            }

            CollectPlugins(skeleton, mergedPlugins, seenPluginKeys);

            // Copy SecurityRoles and Workflows from this solution's directory
            var solutionDir = Path.GetDirectoryName(metadataFile)!;
            CopySecurityRoleFiles(Path.Combine(solutionDir, "SecurityRoles"), securityRolesDir, seenRoleIds,
                includeRolePrivileges);
            CopyWorkflowFiles(Path.Combine(solutionDir, "Workflows"), workflowsDir, seenWorkflowIds);

            // Convert solution export workflows/business rules (XAML format) to DataContract format
            ConvertSolutionExportWorkflows(solutionDir, workflowsDir, seenWorkflowIds);
        }

        combined!.Plugins = mergedPlugins;

        // Normalise MetaPlugin fields to the convention MetadataRegistrationStrategy expects:
        //   AssemblyName          = fully-qualified type name   (e.g. "My.Assembly.MyPlugin")
        //   PluginTypeAssemblyName = short assembly name         (e.g. "My.Assembly")
        // Legacy MetadataSync exports stored these in the opposite order.  Detect the swap by
        // checking whether PluginTypeAssemblyName starts with AssemblyName + "." which means
        // PluginTypeAssemblyName holds the type name and AssemblyName holds the assembly name.
        NormalizePluginFields(combined!);

        // Exclude plugin steps that cannot function against the emulator (see method doc).
        // Emulator-only — the real solution export/registration in CRM is untouched.
        ExcludeUnsupportedPlugins(combined!, excludedPluginTypeNames);

        // Ensure required system entities exist (XrmMockup needs these to initialize)
        EnsureRequiredSystemEntities(combined!);

        // Ensure every N:N relationship has its intersect entity in metadata, so Associate /
        // RetrieveMultiple against the intersect works (solution exports omit intersect entities).
        EnsureManyToManyIntersectEntities(combined!);

        // Give every entity non-null relationship collections (see method doc).
        EnsureRelationshipCollections(combined!);

        // Add columns that are staged in _pending/ but not yet committed to CRM (see method doc).
        ApplyPendingAttributes(combined!, solutionExportsPath);

        // Write combined Metadata.xml
        var outputPath = Path.Combine(outputDir, "Metadata.xml");
        using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        {
            serializer.WriteObject(stream, combined);
        }

        return outputDir;
    }

    private static List<string> FindMetadataFiles(string rootPath)
    {
        var results = new List<string>();

        if (!Directory.Exists(rootPath))
            return results;

        // Look for Metadata.xml files at any depth, but skip _* directories
        foreach (var dir in Directory.GetDirectories(rootPath))
        {
            var dirName = Path.GetFileName(dir);
            if (dirName.StartsWith('_')) continue;

            // Check if this directory has a Metadata.xml
            var metadataFile = Path.Combine(dir, "Metadata.xml");
            if (File.Exists(metadataFile))
            {
                results.Add(metadataFile);
            }

            // Also recurse one level for nested structures
            foreach (var subDir in Directory.GetDirectories(dir))
            {
                var subDirName = Path.GetFileName(subDir);
                if (subDirName.StartsWith('_')) continue;

                var subMetadataFile = Path.Combine(subDir, "Metadata.xml");
                if (File.Exists(subMetadataFile))
                {
                    results.Add(subMetadataFile);
                }
            }
        }

        // Also check root itself
        var rootMetadata = Path.Combine(rootPath, "Metadata.xml");
        if (File.Exists(rootMetadata))
        {
            results.Add(rootMetadata);
        }

        return results;
    }

    /// <summary>
    /// Appends a solution's plugin steps to the merged list, skipping ones already contributed by
    /// an earlier solution. The same step can legitimately be exported from two solutions when both
    /// include the plugin assembly; XrmMockup would then run it twice on a single message.
    /// Identity is the registration itself (type + message + entity + stage + mode + rank +
    /// filtering attributes), not the step Name, which is not unique across assemblies.
    /// </summary>
    private static void CollectPlugins(
        MetadataSkeleton skeleton,
        List<MetaPlugin> mergedPlugins,
        HashSet<string> seenPluginKeys)
    {
        if (skeleton.Plugins == null) return;

        foreach (var plugin in skeleton.Plugins)
        {
            var key = string.Join("|",
                plugin.AssemblyName,
                plugin.PluginTypeAssemblyName,
                plugin.MessageName,
                plugin.PrimaryEntity,
                plugin.Stage,
                plugin.Mode,
                plugin.Rank,
                plugin.FilteredAttributes);

            if (seenPluginKeys.Add(key))
                mergedPlugins.Add(plugin);
        }
    }

    private static void NormalizePluginFields(MetadataSkeleton skeleton)
    {
        if (skeleton.Plugins == null) return;

        foreach (var plugin in skeleton.Plugins)
        {
            if (!string.IsNullOrEmpty(plugin.AssemblyName)
                && !string.IsNullOrEmpty(plugin.PluginTypeAssemblyName)
                && plugin.PluginTypeAssemblyName.StartsWith(plugin.AssemblyName + ".", StringComparison.Ordinal))
            {
                (plugin.AssemblyName, plugin.PluginTypeAssemblyName) =
                    (plugin.PluginTypeAssemblyName, plugin.AssemblyName);
            }
        }
    }

    private static void ExcludeUnsupportedPlugins(MetadataSkeleton skeleton, IEnumerable<string>? excludedPluginTypeNames)
    {
        if (skeleton.Plugins == null || excludedPluginTypeNames == null) return;

        var excluded = new HashSet<string>(excludedPluginTypeNames, StringComparer.Ordinal);
        if (excluded.Count == 0) return;

        skeleton.Plugins = skeleton.Plugins
            .Where(p => !excluded.Contains(p.AssemblyName))
            .ToList();
    }

    /// <summary>
    /// XrmMockup requires certain system entities to initialize (businessunit, systemuser, team,
    /// teammembership, transactioncurrency, organization). If any are missing from the merged
    /// metadata, inject minimal stubs so XrmMockup can boot.
    /// </summary>
    private static void EnsureRequiredSystemEntities(MetadataSkeleton skeleton)
    {
        skeleton.EntityMetadata ??= new Dictionary<string, EntityMetadata>();
        skeleton.DefaultStateStatus ??= new Dictionary<string, Dictionary<int, int>>();

        // teammembership is an intersect entity that XrmMockup uses for team member tracking
        EnsureEntity(skeleton, "teammembership", OwnershipTypes.None,
            CreateAttribute<UniqueIdentifierAttributeMetadata>("teammembershipid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<LookupAttributeMetadata>("teamid", AttributeTypeCode.Lookup),
            CreateAttribute<LookupAttributeMetadata>("systemuserid", AttributeTypeCode.Lookup));

        // Other required system entities — only add if completely missing
        EnsureEntity(skeleton, "businessunit", OwnershipTypes.BusinessOwned, "name",
            CreateAttribute<UniqueIdentifierAttributeMetadata>("businessunitid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<StringAttributeMetadata>("name", AttributeTypeCode.String),
            CreateAttribute<LookupAttributeMetadata>("parentbusinessunitid", AttributeTypeCode.Lookup));

        EnsureEntity(skeleton, "systemuser", OwnershipTypes.BusinessOwned, "fullname",
            CreateAttribute<UniqueIdentifierAttributeMetadata>("systemuserid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<StringAttributeMetadata>("firstname", AttributeTypeCode.String),
            CreateAttribute<StringAttributeMetadata>("lastname", AttributeTypeCode.String),
            CreateAttribute<StringAttributeMetadata>("fullname", AttributeTypeCode.String),
            CreateAttribute<LookupAttributeMetadata>("businessunitid", AttributeTypeCode.Lookup));

        EnsureEntity(skeleton, "team", OwnershipTypes.BusinessOwned, "name",
            CreateAttribute<UniqueIdentifierAttributeMetadata>("teamid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<StringAttributeMetadata>("name", AttributeTypeCode.String),
            CreateAttribute<LookupAttributeMetadata>("businessunitid", AttributeTypeCode.Lookup),
            CreateAttribute<LookupAttributeMetadata>("administratorid", AttributeTypeCode.Lookup));

        EnsureEntity(skeleton, "transactioncurrency", OwnershipTypes.OrganizationOwned, "currencyname",
            CreateAttribute<UniqueIdentifierAttributeMetadata>("transactioncurrencyid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<StringAttributeMetadata>("currencyname", AttributeTypeCode.String),
            CreateAttribute<StringAttributeMetadata>("isocurrencycode", AttributeTypeCode.String),
            CreateAttribute<DecimalAttributeMetadata>("exchangerate", AttributeTypeCode.Decimal),
            CreateAttribute<IntegerAttributeMetadata>("currencyprecision", AttributeTypeCode.Integer));

        EnsureEntity(skeleton, "organization", OwnershipTypes.OrganizationOwned, "name",
            CreateAttribute<UniqueIdentifierAttributeMetadata>("organizationid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<StringAttributeMetadata>("name", AttributeTypeCode.String));

        EnsureEntity(skeleton, "role", OwnershipTypes.BusinessOwned, "name",
            CreateAttribute<UniqueIdentifierAttributeMetadata>("roleid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<StringAttributeMetadata>("name", AttributeTypeCode.String),
            CreateAttribute<LookupAttributeMetadata>("businessunitid", AttributeTypeCode.Lookup),
            CreateAttribute<LookupAttributeMetadata>("roletemplateid", AttributeTypeCode.Lookup),
            CreateAttribute<LookupAttributeMetadata>("createdby", AttributeTypeCode.Lookup),
            CreateAttribute<LookupAttributeMetadata>("modifiedby", AttributeTypeCode.Lookup),
            CreateAttribute<DateTimeAttributeMetadata>("createdon", AttributeTypeCode.DateTime),
            CreateAttribute<DateTimeAttributeMetadata>("modifiedon", AttributeTypeCode.DateTime));

        EnsureEntity(skeleton, "roletemplate", OwnershipTypes.None, "name",
            CreateAttribute<UniqueIdentifierAttributeMetadata>("roletemplateid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<StringAttributeMetadata>("name", AttributeTypeCode.String));

        // Intersect entities for security role assignments.
        // The intersect columns must be Uniqueidentifier, not Lookup: XrmMockup writes and
        // reads them as raw Guids (AssociateRequestHandler, DisassociateRequestHandler,
        // Core's N:N traversal all use GetColumn<Guid>). A Lookup column only accepts a
        // DbRow, so a Guid write is silently dropped and the next read NREs on unboxing null
        // — which surfaces as a crash when assigning a user their second role.
        EnsureEntity(skeleton, "systemuserroles", OwnershipTypes.None,
            CreateAttribute<UniqueIdentifierAttributeMetadata>("systemuserrolesid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<UniqueIdentifierAttributeMetadata>("systemuserid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<UniqueIdentifierAttributeMetadata>("roleid", AttributeTypeCode.Uniqueidentifier));

        EnsureEntity(skeleton, "teamroles", OwnershipTypes.None,
            CreateAttribute<UniqueIdentifierAttributeMetadata>("teamrolesid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<UniqueIdentifierAttributeMetadata>("teamid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<UniqueIdentifierAttributeMetadata>("roleid", AttributeTypeCode.Uniqueidentifier));

        // Principal object access for sharing
        EnsureEntity(skeleton, "principalobjectaccess", OwnershipTypes.None,
            CreateAttribute<UniqueIdentifierAttributeMetadata>("principalobjectaccessid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<LookupAttributeMetadata>("principalid", AttributeTypeCode.Lookup),
            CreateAttribute<LookupAttributeMetadata>("objectid", AttributeTypeCode.Lookup),
            CreateAttribute<IntegerAttributeMetadata>("accessrightsmask", AttributeTypeCode.Integer),
            CreateAttribute<StringAttributeMetadata>("objecttypecode", AttributeTypeCode.String));

        // Environment variable tables — needed by any plugin that calls GetEnvironmentVariable.
        // These are standard Dataverse entities but are not included in solution exports.
        EnsureEntity(skeleton, "environmentvariabledefinition", OwnershipTypes.OrganizationOwned, "schemaname",
            CreateAttribute<UniqueIdentifierAttributeMetadata>("environmentvariabledefinitionid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<StringAttributeMetadata>("schemaname", AttributeTypeCode.String),
            CreateAttribute<StringAttributeMetadata>("defaultvalue", AttributeTypeCode.String),
            CreateAttribute<StringAttributeMetadata>("displayname", AttributeTypeCode.String),
            CreateAttribute<IntegerAttributeMetadata>("type", AttributeTypeCode.Integer));

        EnsureEntity(skeleton, "environmentvariablevalue", OwnershipTypes.OrganizationOwned, "value",
            CreateAttribute<UniqueIdentifierAttributeMetadata>("environmentvariablevalueid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<LookupAttributeMetadata>("environmentvariabledefinitionid", AttributeTypeCode.Lookup),
            CreateAttribute<StringAttributeMetadata>("value", AttributeTypeCode.String));

        // Saved views (public/system) — needed by any plugin that looks up a view's FetchXML.
        // Standard Dataverse entity, not included in solution exports. statecode/statuscode are
        // required (not just DefaultStateStatus) — CreateRequestHandler only auto-populates them
        // on Create when both attributes are present in metadata (Utility.IsValidAttribute).
        EnsureEntity(skeleton, "savedquery", OwnershipTypes.OrganizationOwned, "name",
            CreateAttribute<UniqueIdentifierAttributeMetadata>("savedqueryid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<StringAttributeMetadata>("name", AttributeTypeCode.String),
            CreateAttribute<StringAttributeMetadata>("returnedtypecode", AttributeTypeCode.String),
            CreateAttribute<StringAttributeMetadata>("fetchxml", AttributeTypeCode.String),
            CreateAttribute<StateAttributeMetadata>("statecode", AttributeTypeCode.State),
            CreateAttribute<StatusAttributeMetadata>("statuscode", AttributeTypeCode.Status));

        // activitypointer — the base table every activity row is mirrored into. XrmMockup writes a
        // pointer row on Create and Update of any entity whose metadata has IsActivity set
        // (kf_booking, phonecall, task, appointment, …), so creating one fails outright when the
        // entity is missing. Solution exports never contain it. Attributes mirror exactly what
        // XrmMockup copies across; PrimaryIdAttribute is activityid, not the derived default.
        EnsureEntity(skeleton, "activitypointer", OwnershipTypes.UserOwned, "subject",
            CreateAttribute<UniqueIdentifierAttributeMetadata>("activityid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<StringAttributeMetadata>("subject", AttributeTypeCode.String),
            CreateAttribute<MemoAttributeMetadata>("description", AttributeTypeCode.Memo),
            CreateAttribute<PicklistAttributeMetadata>("activitytypecode", AttributeTypeCode.Picklist),
            CreateAttribute<PicklistAttributeMetadata>("prioritycode", AttributeTypeCode.Picklist),
            CreateAttribute<PicklistAttributeMetadata>("deliveryprioritycode", AttributeTypeCode.Picklist),
            CreateAttribute<LookupAttributeMetadata>("ownerid", AttributeTypeCode.Owner),
            CreateAttribute<LookupAttributeMetadata>("owningbusinessunit", AttributeTypeCode.Lookup),
            CreateAttribute<LookupAttributeMetadata>("owninguser", AttributeTypeCode.Lookup),
            CreateAttribute<LookupAttributeMetadata>("owningteam", AttributeTypeCode.Lookup),
            CreateAttribute<LookupAttributeMetadata>("regardingobjectid", AttributeTypeCode.Lookup),
            CreateAttribute<IntegerAttributeMetadata>("actualdurationminutes", AttributeTypeCode.Integer),
            CreateAttribute<IntegerAttributeMetadata>("scheduleddurationminutes", AttributeTypeCode.Integer),
            CreateAttribute<DateTimeAttributeMetadata>("actualstart", AttributeTypeCode.DateTime),
            CreateAttribute<DateTimeAttributeMetadata>("actualend", AttributeTypeCode.DateTime),
            CreateAttribute<DateTimeAttributeMetadata>("scheduledstart", AttributeTypeCode.DateTime),
            CreateAttribute<DateTimeAttributeMetadata>("scheduledend", AttributeTypeCode.DateTime),
            CreateAttribute<DateTimeAttributeMetadata>("senton", AttributeTypeCode.DateTime),
            CreateAttribute<BooleanAttributeMetadata>("isbilled", AttributeTypeCode.Boolean),
            CreateAttribute<BooleanAttributeMetadata>("isregularactivity", AttributeTypeCode.Boolean),
            CreateAttribute<BooleanAttributeMetadata>("isworkflowcreated", AttributeTypeCode.Boolean),
            CreateAttribute<StateAttributeMetadata>("statecode", AttributeTypeCode.State),
            CreateActivityStatusAttribute());

        if (skeleton.EntityMetadata.TryGetValue("activitypointer", out var activityPointer))
            SetMetadataProperty(activityPointer, "PrimaryIdAttribute", "activityid");

        // Ensure BaseOrganization entity exists
        if (skeleton.BaseOrganization == null || skeleton.BaseOrganization.Attributes.Count == 0)
        {
            skeleton.BaseOrganization = new Entity("organization");
        }
    }

    /// <summary>
    /// For every many-to-many relationship found on any entity, ensure the intersect entity
    /// exists in metadata. Solution exports describe the relationship on each participating
    /// entity but do not export the intersect entity itself; without it XrmMockup's Associate
    /// and intersect-table queries fail with "No EntityMetadata found".
    /// </summary>
    private static void EnsureManyToManyIntersectEntities(MetadataSkeleton skeleton)
    {
        if (skeleton.EntityMetadata == null) return;

        foreach (var entity in skeleton.EntityMetadata.Values.ToList())
        {
            if (entity.ManyToManyRelationships == null) continue;

            foreach (var rel in entity.ManyToManyRelationships)
            {
                if (string.IsNullOrEmpty(rel.IntersectEntityName)
                    || string.IsNullOrEmpty(rel.Entity1IntersectAttribute)
                    || string.IsNullOrEmpty(rel.Entity2IntersectAttribute)
                    || skeleton.EntityMetadata.ContainsKey(rel.IntersectEntityName))
                    continue;

                EnsureEntity(skeleton, rel.IntersectEntityName, OwnershipTypes.None,
                    CreateAttribute<UniqueIdentifierAttributeMetadata>(rel.IntersectEntityName + "id", AttributeTypeCode.Uniqueidentifier),
                    CreateAttribute<UniqueIdentifierAttributeMetadata>(rel.Entity1IntersectAttribute, AttributeTypeCode.Uniqueidentifier),
                    CreateAttribute<UniqueIdentifierAttributeMetadata>(rel.Entity2IntersectAttribute, AttributeTypeCode.Uniqueidentifier));
            }
        }
    }

    /// <summary>
    /// Guarantees every entity has non-null OneToMany / ManyToOne / ManyToMany collections.
    ///
    /// Synthesized entities (intersect tables, system stubs) are built attribute-by-attribute and
    /// never get relationship collections, and a solution export can omit them too. XrmMockup reads
    /// them unguarded — cascading an owner change walks the owner's 1:N relationships and calls
    /// FirstOrDefault on the related entity's collections — so a single null there surfaces as an
    /// opaque ArgumentNullException("source") from deep inside LINQ, with nothing naming the entity.
    /// Empty is the honest value: the entity genuinely has no relationships we know about.
    /// </summary>
    private static void EnsureRelationshipCollections(MetadataSkeleton skeleton)
    {
        if (skeleton.EntityMetadata == null) return;

        foreach (var entity in skeleton.EntityMetadata.Values)
        {
            if (entity.OneToManyRelationships == null)
                SetMetadataProperty(entity, "OneToManyRelationships", Array.Empty<OneToManyRelationshipMetadata>());
            if (entity.ManyToOneRelationships == null)
                SetMetadataProperty(entity, "ManyToOneRelationships", Array.Empty<OneToManyRelationshipMetadata>());
            if (entity.ManyToManyRelationships == null)
                SetMetadataProperty(entity, "ManyToManyRelationships", Array.Empty<ManyToManyRelationshipMetadata>());
        }
    }

    /// <summary>
    /// Adds columns that are staged in <c>_pending/Attributes/</c> but not yet in CRM.
    ///
    /// Without this, code for a new column cannot be run or tested until someone has committed the
    /// column and re-exported the metadata: XrmMockup validates every attribute against the exported
    /// metadata and rejects the write with "'lead' entity doesn't contain attribute with Name = …".
    /// That ordering is backwards — the column is staged precisely so the code that uses it can be
    /// written — and it pushes developers towards committing metadata to get a green test run.
    ///
    /// Only additive, and only for columns metadata does not already describe: once the real column
    /// arrives in the export, the export wins and this does nothing. A staged file is deleted by
    /// <c>commit</c>, so <c>_pending/</c> stays an accurate list of what is coming.
    ///
    /// Picklist options are resolved from the staged definition — a local <c>options</c> array, or a
    /// global option set staged alongside under <c>_pending/GlobalOptionSets/</c>. An option set that
    /// is not staged locally yields an empty one, which is enough for XrmMockup to accept writes.
    /// </summary>
    private static void ApplyPendingAttributes(MetadataSkeleton skeleton, string solutionExportsPath)
    {
        if (!Directory.Exists(solutionExportsPath))
            return;

        foreach (var file in Directory.EnumerateFiles(solutionExportsPath, "*.attribute.json",
                     SearchOption.AllDirectories))
        {
            // Only the pending queue — an archived copy under _committed/ describes a column the
            // export already carries.
            if (!file.Replace('\\', '/').Contains("/_pending/Attributes/"))
                continue;

            var definition = JsonNode.Parse(File.ReadAllText(file))?.AsObject();
            var entityName = definition?["entityLogicalName"]?.GetValue<string>();
            var attributeName = definition?["attributeLogicalName"]?.GetValue<string>();
            if (definition == null || string.IsNullOrEmpty(entityName) || string.IsNullOrEmpty(attributeName))
                continue;

            // An entity the loaded solutions don't describe at all is out of scope here — there is no
            // EntityMetadata to extend, and synthesising one would hide a missing export.
            if (!skeleton.EntityMetadata.TryGetValue(entityName, out var entity) || entity.Attributes == null)
                continue;

            if (entity.Attributes.Any(a =>
                    string.Equals(a.LogicalName, attributeName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var attribute = CreatePendingAttribute(definition, attributeName, file);
            SetMetadataProperty(entity, "Attributes", entity.Attributes.Append(attribute).ToArray());
        }
    }

    private static AttributeMetadata CreatePendingAttribute(
        JsonObject definition, string attributeName, string file)
    {
        var type = definition["attributeType"]?.GetValue<string>()?.ToLowerInvariant();

        switch (type)
        {
            case "picklist":
                var picklist = CreateAttribute<PicklistAttributeMetadata>(attributeName, AttributeTypeCode.Picklist);
                SetMetadataProperty(picklist, "OptionSet", BuildPendingOptionSet(definition, file));
                return picklist;

            case "boolean":
                var boolean = CreateAttribute<BooleanAttributeMetadata>(attributeName, AttributeTypeCode.Boolean);
                SetMetadataProperty(boolean, "OptionSet", new BooleanOptionSetMetadata(
                    new OptionMetadata(MakeLabel("Ja"), 1),
                    new OptionMetadata(MakeLabel("Nej"), 0)));
                return boolean;

            case "string":
                return CreateAttribute<StringAttributeMetadata>(attributeName, AttributeTypeCode.String);
            case "memo":
                return CreateAttribute<MemoAttributeMetadata>(attributeName, AttributeTypeCode.Memo);
            case "int":
            case "integer":
                return CreateAttribute<IntegerAttributeMetadata>(attributeName, AttributeTypeCode.Integer);
            case "decimal":
                return CreateAttribute<DecimalAttributeMetadata>(attributeName, AttributeTypeCode.Decimal);
            case "money":
                return CreateAttribute<MoneyAttributeMetadata>(attributeName, AttributeTypeCode.Money);
            case "datetime":
                return CreateAttribute<DateTimeAttributeMetadata>(attributeName, AttributeTypeCode.DateTime);

            case "lookup":
                var lookup = CreateAttribute<LookupAttributeMetadata>(attributeName, AttributeTypeCode.Lookup);
                var targets = definition["targetEntityLogicalNames"]?.AsArray()
                        ?.Select(t => t?.GetValue<string>()).Where(t => !string.IsNullOrEmpty(t)).ToArray()
                    ?? [definition["targetEntityLogicalName"]?.GetValue<string>()];
                SetMetadataProperty(lookup, "Targets", targets.Where(t => !string.IsNullOrEmpty(t)).ToArray());
                return lookup;

            default:
                // Loud on purpose: a silently skipped column reappears as the same confusing
                // "entity doesn't contain attribute" failure this method exists to prevent.
                throw new NotSupportedException(
                    $"Staged attribute '{attributeName}' in '{file}' has type '{type}', which " +
                    $"{nameof(ApplyPendingAttributes)} cannot synthesize yet. Add it there.");
        }
    }

    private static OptionSetMetadata BuildPendingOptionSet(JsonObject definition, string file)
    {
        var optionSet = new OptionSetMetadata();

        // Local (entity-scoped) options are written straight into the attribute definition.
        var localOptions = definition["options"]?.AsArray();
        if (localOptions != null)
        {
            foreach (var option in localOptions)
                AddOption(optionSet, option?["value"]?.GetValue<int>(), option?["label"]?.GetValue<string>());
            return optionSet;
        }

        // A global option set lives in its own staged file next to the attribute's.
        var optionSetName = definition["optionSetName"]?.GetValue<string>();
        if (string.IsNullOrEmpty(optionSetName))
            return optionSet;

        SetMetadataProperty(optionSet, "Name", optionSetName);
        SetMetadataProperty(optionSet, "IsGlobal", true);

        var pendingRoot = Path.GetDirectoryName(Path.GetDirectoryName(file));
        var globalFile = pendingRoot == null
            ? null
            : Path.Combine(pendingRoot, "GlobalOptionSets", optionSetName + ".optionset.json");
        if (globalFile == null || !File.Exists(globalFile))
            return optionSet;

        var globalDefinition = JsonNode.Parse(File.ReadAllText(globalFile))?.AsObject();
        foreach (var value in globalDefinition?["values"]?.AsArray() ?? [])
            AddOption(optionSet, value?["value"]?.GetValue<int>(), value?["label"]?.GetValue<string>());

        return optionSet;

        static void AddOption(OptionSetMetadata target, int? value, string? label)
        {
            if (value.HasValue)
                target.Options.Add(new OptionMetadata(MakeLabel(label ?? value.Value.ToString()), value));
        }
    }

    /// <summary>
    /// A label with <see cref="Label.UserLocalizedLabel"/> filled in, not just the localised list.
    /// The platform populates both on retrieve, and XrmMockup follows it: reading a picklist column
    /// dereferences the option's UserLocalizedLabel to build the formatted value, so a label built
    /// only from the constructor throws NullReferenceException from inside a parallel loop, arriving
    /// as an AggregateException with nothing in it naming the column.
    /// </summary>
    private static Label MakeLabel(string text)
    {
        var localized = new LocalizedLabel(text, OrgBaseLcid);
        var label = new Label(text, OrgBaseLcid);
        SetMetadataProperty(label, "UserLocalizedLabel", localized);
        return label;
    }

    /// <summary>
    /// kf-dev's base language. Only ever seen in labels synthesized here, which nothing asserts on —
    /// the org's real labels come from the export.
    /// </summary>
    private const int OrgBaseLcid = 1030;

    private static void EnsureEntity(MetadataSkeleton skeleton, string logicalName,
        OwnershipTypes ownership, params AttributeMetadata[] attributes)
        => EnsureEntity(skeleton, logicalName, ownership, primaryNameAttribute: null, attributes);

    /// <summary>
    /// Synthesizes minimal EntityMetadata for a standard entity missing from the solution export.
    /// Setting PrimaryNameAttribute (when the entity has a natural name column) is required even
    /// for entities never displayed directly: XrmMockup's PopulateEntityReferenceNames resolves
    /// the .Name of every EntityReference by looking up the *target* entity's PrimaryNameAttribute,
    /// and throws ArgumentNullException (surfaced to callers as an opaque 406) if it's null and any
    /// row anywhere holds a lookup pointing at this entity.
    /// </summary>
    private static void EnsureEntity(MetadataSkeleton skeleton, string logicalName,
        OwnershipTypes ownership, string? primaryNameAttribute, params AttributeMetadata[] attributes)
    {
        if (skeleton.EntityMetadata.ContainsKey(logicalName))
            return;

        var entityMetadata = new EntityMetadata();
        SetMetadataProperty(entityMetadata, "LogicalName", logicalName);
        SetMetadataProperty(entityMetadata, "OwnershipType", ownership);
        SetMetadataProperty(entityMetadata, "PrimaryIdAttribute", logicalName + "id");
        if (primaryNameAttribute != null)
        {
            SetMetadataProperty(entityMetadata, "PrimaryNameAttribute", primaryNameAttribute);
        }

        // Set attributes via reflection (read-only property)
        SetMetadataProperty(entityMetadata, "Attributes", attributes);

        skeleton.EntityMetadata[logicalName] = entityMetadata;

        // Add default state/status (Active=0 -> Active=1)
        if (!skeleton.DefaultStateStatus.ContainsKey(logicalName))
        {
            skeleton.DefaultStateStatus[logicalName] = new Dictionary<int, int> { { 0, 1 } };
        }
    }

    /// <summary>
    /// activitypointer's statuscode needs real options, unlike the other stubs. Every mirrored
    /// activity row arrives with an explicit statuscode, and XrmMockup then validates it by looking
    /// up the matching option's State — a statuscode attribute with no OptionSet throws there.
    /// States 0-3 map to status reasons 1-4, the same mapping XrmMockup applies when it builds the
    /// pointer row.
    /// </summary>
    private static StatusAttributeMetadata CreateActivityStatusAttribute()
    {
        var attr = CreateAttribute<StatusAttributeMetadata>("statuscode", AttributeTypeCode.Status);
        var optionSet = new OptionSetMetadata();
        for (var state = 0; state <= 3; state++)
            optionSet.Options.Add(new StatusOptionMetadata { Value = state + 1, State = state });
        SetMetadataProperty(attr, "OptionSet", optionSet);
        return attr;
    }

    private static T CreateAttribute<T>(string logicalName, AttributeTypeCode typeCode) where T : AttributeMetadata, new()
    {
        var attr = new T();
        SetMetadataProperty(attr, "LogicalName", logicalName);
        SetMetadataProperty(attr, "AttributeType", typeCode);
        return attr;
    }

    private static void SetMetadataProperty(object target, string propertyName, object value)
    {
        var prop = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(target, value);
            return;
        }

        // SDK metadata uses internal setters — access via reflection
        if (prop != null)
        {
            var setter = prop.GetSetMethod(true);
            if (setter != null)
            {
                setter.Invoke(target, [value]);
                return;
            }
        }

        // Try backing field
        var fieldName = char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
        var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? target.GetType().GetField("_" + fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(target, value);
    }

    /// <summary>
    /// Scans SolutionExport/*/Workflows/ for .xaml.data.xml files paired with .xaml files,
    /// converts them to DataContract-serialized Entity objects that XrmMockup can load.
    /// </summary>
    private static void ConvertSolutionExportWorkflows(string solutionDir, string workflowsDir, HashSet<Guid> seenWorkflowIds)
    {
        // Find SolutionExport directories
        var solutionExportDir = Path.Combine(solutionDir, "SolutionExport");
        if (!Directory.Exists(solutionExportDir)) return;

        foreach (var solDir in Directory.GetDirectories(solutionExportDir))
        {
            var wfDir = Path.Combine(solDir, "Workflows");
            if (!Directory.Exists(wfDir)) continue;

            foreach (var dataFile in Directory.GetFiles(wfDir, "*.xaml.data.xml"))
            {
                // Find matching XAML file
                var xamlFile = dataFile.Replace(".data.xml", "");
                if (!File.Exists(xamlFile)) continue;

                try
                {
                    var workflowEntity = ConvertWorkflowToEntity(dataFile, xamlFile);
                    if (workflowEntity == null) continue;

                    // Skip if this workflow id was already emitted (plain copy or another solution).
                    if (!seenWorkflowIds.Add(workflowEntity.Id)) continue;

                    // Write as DataContract-serialized Entity
                    var outputName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(dataFile)));
                    var outputPath = Path.Combine(workflowsDir, outputName + ".xml");
                    if (File.Exists(outputPath)) continue; // Don't overwrite

                    var serializer = new DataContractSerializer(typeof(Entity));
                    using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                    serializer.WriteObject(stream, workflowEntity);
                }
                catch
                {
                    // Skip workflows that fail to convert
                }
            }
        }
    }

    /// <summary>
    /// Converts a solution export workflow .data.xml + .xaml pair into a DataContract Entity
    /// that XrmMockup's Utility.GetWorkflow can load.
    /// </summary>
    private static Entity? ConvertWorkflowToEntity(string dataFile, string xamlFile)
    {
        var doc = XDocument.Load(dataFile);
        var root = doc.Root;
        if (root == null) return null;

        var workflowId = root.Attribute("WorkflowId")?.Value;
        if (string.IsNullOrEmpty(workflowId)) return null;

        var id = Guid.Parse(workflowId.Trim('{', '}'));
        var name = root.Attribute("Name")?.Value ?? "";
        var primaryEntity = root.Element("PrimaryEntity")?.Value?.ToLowerInvariant() ?? "";
        var categoryStr = root.Element("Category")?.Value ?? "0";
        var modeStr = root.Element("Mode")?.Value ?? "1";
        var stateCodeStr = root.Element("StateCode")?.Value ?? "1";

        // Only include activated workflows/business rules
        if (stateCodeStr != "1") return null;

        var category = int.Parse(categoryStr);
        var mode = int.Parse(modeStr);

        var xaml = File.ReadAllText(xamlFile);

        var entity = new Entity("workflow", id);
        entity["name"] = name;
        entity["primaryentity"] = primaryEntity;
        entity["category"] = new OptionSetValue(category);
        entity["mode"] = new OptionSetValue(mode);
        entity["statecode"] = new OptionSetValue(1); // Activated
        entity["statuscode"] = new OptionSetValue(2); // Activated
        entity["xaml"] = xaml;

        // ownerid is required by WorkflowConstructor.Parse — use a dummy system user
        entity["ownerid"] = new EntityReference("systemuser", Guid.Empty);

        // runas: 1 = Calling User (default for business rules)
        var runAsStr = root.Element("RunAs")?.Value ?? "1";
        entity["runas"] = new OptionSetValue(int.Parse(runAsStr));

        // Parse trigger flags
        entity["triggeroncreate"] = root.Element("TriggerOnCreate")?.Value == "1";
        entity["triggerondelete"] = root.Element("TriggerOnDelete")?.Value == "1";

        // Parse scope
        var scopeStr = root.Element("Scope")?.Value ?? "1";
        entity["scope"] = new OptionSetValue(int.Parse(scopeStr));

        // Parse trigger on update attribute list if present
        var triggerOnUpdateAttrs = root.Element("TriggerOnUpdateAttributeList")?.Value;
        if (!string.IsNullOrEmpty(triggerOnUpdateAttrs))
            entity["triggeronupdateattributelist"] = triggerOnUpdateAttrs;

        // Parse form scope for business rules
        var processTriggerFormId = root.Element("ProcessTriggerFormId")?.Value;
        if (!string.IsNullOrEmpty(processTriggerFormId) && Guid.TryParse(processTriggerFormId.Trim('{', '}'), out var formId))
            entity["processtriggerformid"] = formId;

        return entity;
    }

    /// <summary>
    /// Copies security role XML files, deduplicating by RoleId so that the same role appearing
    /// in multiple solutions only gets copied once. XrmMockup's Security ctor does
    /// ToDictionary(s => s.RoleId) and crashes if the same GUID appears twice.
    /// </summary>
    private static void CopySecurityRoleFiles(
        string sourceDir, string destDir, HashSet<Guid> seenRoleIds, bool includeRolePrivileges)
    {
        if (!Directory.Exists(sourceDir)) return;

        var roleSerializer = new DataContractSerializer(typeof(SecurityRole));
        foreach (var file in Directory.GetFiles(sourceDir, "*.xml"))
        {
            Guid roleId;
            SecurityRole role;
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                role = (SecurityRole)roleSerializer.ReadObject(stream)!;
                roleId = role.RoleId;
            }
            catch
            {
                // Can't parse — fall back to filename-based dedup
                var fallbackDest = Path.Combine(destDir, Path.GetFileName(file));
                if (!File.Exists(fallbackDest))
                    File.Copy(file, fallbackDest);
                continue;
            }

            if (!seenRoleIds.Add(roleId))
                continue; // Same RoleId already written from a previous solution

            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            if (File.Exists(destFile))
                destFile = Path.Combine(destDir, roleId.ToString("N") + ".xml");

            if (includeRolePrivileges)
            {
                File.Copy(file, destFile);
            }
            else
            {
                role.Privileges = [];
                using var outStream = new FileStream(destFile, FileMode.Create, FileAccess.Write);
                roleSerializer.WriteObject(outStream, role);
            }
        }
    }

    private static readonly XNamespace ContractsNs = "http://schemas.microsoft.com/xrm/2011/Contracts";

    /// <summary>
    /// Copies plain Workflows/*.xml (DataContract-serialized Entity) into the merged folder,
    /// skipping any workflow whose entity id was already emitted (from another solution or the
    /// xaml-conversion path). Dedup is by entity id — the same value XrmMockup keys its DB on —
    /// so a workflow committed to two solutions is loaded only once.
    /// </summary>
    private static void CopyWorkflowFiles(string sourceDir, string destDir, HashSet<Guid> seenWorkflowIds)
    {
        if (!Directory.Exists(sourceDir)) return;

        foreach (var file in Directory.GetFiles(sourceDir, "*.xml"))
        {
            var id = TryGetWorkflowEntityId(file);
            if (id.HasValue && !seenWorkflowIds.Add(id.Value))
                continue; // already emitted this workflow id

            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            // Don't overwrite — first solution's version wins (same as Merge behavior)
            if (!File.Exists(destFile))
            {
                File.Copy(file, destFile);
            }
        }
    }

    /// <summary>Reads the entity id from a DataContract-serialized workflow Entity xml, or null if unparseable.</summary>
    private static Guid? TryGetWorkflowEntityId(string file)
    {
        try
        {
            var idValue = XDocument.Load(file).Root?.Element(ContractsNs + "Id")?.Value;
            if (Guid.TryParse(idValue, out var id)) return id;
        }
        catch
        {
            // Unparseable — fall back to filename-based copy (no dedup).
        }
        return null;
    }
}
