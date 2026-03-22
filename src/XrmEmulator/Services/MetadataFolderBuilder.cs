using System.Reflection;
using System.Runtime.Serialization;
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
    public static string BuildCombinedMetadataFolder(string solutionExportsPath)
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

            // Copy SecurityRoles and Workflows from this solution's directory
            var solutionDir = Path.GetDirectoryName(metadataFile)!;
            CopyXmlFiles(Path.Combine(solutionDir, "SecurityRoles"), securityRolesDir);
            CopyXmlFiles(Path.Combine(solutionDir, "Workflows"), workflowsDir);

            // Convert solution export workflows/business rules (XAML format) to DataContract format
            ConvertSolutionExportWorkflows(solutionDir, workflowsDir);
        }

        // Ensure required system entities exist (XrmMockup needs these to initialize)
        EnsureRequiredSystemEntities(combined!);

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
        EnsureEntity(skeleton, "businessunit", OwnershipTypes.BusinessOwned,
            CreateAttribute<UniqueIdentifierAttributeMetadata>("businessunitid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<StringAttributeMetadata>("name", AttributeTypeCode.String),
            CreateAttribute<LookupAttributeMetadata>("parentbusinessunitid", AttributeTypeCode.Lookup));

        EnsureEntity(skeleton, "systemuser", OwnershipTypes.BusinessOwned,
            CreateAttribute<UniqueIdentifierAttributeMetadata>("systemuserid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<StringAttributeMetadata>("firstname", AttributeTypeCode.String),
            CreateAttribute<StringAttributeMetadata>("lastname", AttributeTypeCode.String),
            CreateAttribute<StringAttributeMetadata>("fullname", AttributeTypeCode.String),
            CreateAttribute<LookupAttributeMetadata>("businessunitid", AttributeTypeCode.Lookup));

        EnsureEntity(skeleton, "team", OwnershipTypes.BusinessOwned,
            CreateAttribute<UniqueIdentifierAttributeMetadata>("teamid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<StringAttributeMetadata>("name", AttributeTypeCode.String),
            CreateAttribute<LookupAttributeMetadata>("businessunitid", AttributeTypeCode.Lookup),
            CreateAttribute<LookupAttributeMetadata>("administratorid", AttributeTypeCode.Lookup));

        EnsureEntity(skeleton, "transactioncurrency", OwnershipTypes.OrganizationOwned,
            CreateAttribute<UniqueIdentifierAttributeMetadata>("transactioncurrencyid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<StringAttributeMetadata>("currencyname", AttributeTypeCode.String),
            CreateAttribute<StringAttributeMetadata>("isocurrencycode", AttributeTypeCode.String),
            CreateAttribute<DecimalAttributeMetadata>("exchangerate", AttributeTypeCode.Decimal),
            CreateAttribute<IntegerAttributeMetadata>("currencyprecision", AttributeTypeCode.Integer));

        EnsureEntity(skeleton, "organization", OwnershipTypes.OrganizationOwned,
            CreateAttribute<UniqueIdentifierAttributeMetadata>("organizationid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<StringAttributeMetadata>("name", AttributeTypeCode.String));

        EnsureEntity(skeleton, "role", OwnershipTypes.BusinessOwned,
            CreateAttribute<UniqueIdentifierAttributeMetadata>("roleid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<StringAttributeMetadata>("name", AttributeTypeCode.String),
            CreateAttribute<LookupAttributeMetadata>("businessunitid", AttributeTypeCode.Lookup),
            CreateAttribute<LookupAttributeMetadata>("roletemplateid", AttributeTypeCode.Lookup),
            CreateAttribute<LookupAttributeMetadata>("createdby", AttributeTypeCode.Lookup),
            CreateAttribute<LookupAttributeMetadata>("modifiedby", AttributeTypeCode.Lookup),
            CreateAttribute<DateTimeAttributeMetadata>("createdon", AttributeTypeCode.DateTime),
            CreateAttribute<DateTimeAttributeMetadata>("modifiedon", AttributeTypeCode.DateTime));

        EnsureEntity(skeleton, "roletemplate", OwnershipTypes.None,
            CreateAttribute<UniqueIdentifierAttributeMetadata>("roletemplateid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<StringAttributeMetadata>("name", AttributeTypeCode.String));

        // Intersect entities for security role assignments
        EnsureEntity(skeleton, "systemuserroles", OwnershipTypes.None,
            CreateAttribute<UniqueIdentifierAttributeMetadata>("systemuserrolesid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<LookupAttributeMetadata>("systemuserid", AttributeTypeCode.Lookup),
            CreateAttribute<LookupAttributeMetadata>("roleid", AttributeTypeCode.Lookup));

        EnsureEntity(skeleton, "teamroles", OwnershipTypes.None,
            CreateAttribute<UniqueIdentifierAttributeMetadata>("teamrolesid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<LookupAttributeMetadata>("teamid", AttributeTypeCode.Lookup),
            CreateAttribute<LookupAttributeMetadata>("roleid", AttributeTypeCode.Lookup));

        // Principal object access for sharing
        EnsureEntity(skeleton, "principalobjectaccess", OwnershipTypes.None,
            CreateAttribute<UniqueIdentifierAttributeMetadata>("principalobjectaccessid", AttributeTypeCode.Uniqueidentifier),
            CreateAttribute<LookupAttributeMetadata>("principalid", AttributeTypeCode.Lookup),
            CreateAttribute<LookupAttributeMetadata>("objectid", AttributeTypeCode.Lookup),
            CreateAttribute<IntegerAttributeMetadata>("accessrightsmask", AttributeTypeCode.Integer),
            CreateAttribute<StringAttributeMetadata>("objecttypecode", AttributeTypeCode.String));

        // Ensure BaseOrganization entity exists
        if (skeleton.BaseOrganization == null || skeleton.BaseOrganization.Attributes.Count == 0)
        {
            skeleton.BaseOrganization = new Entity("organization");
        }
    }

    private static void EnsureEntity(MetadataSkeleton skeleton, string logicalName,
        OwnershipTypes ownership, params AttributeMetadata[] attributes)
    {
        if (skeleton.EntityMetadata.ContainsKey(logicalName))
            return;

        var entityMetadata = new EntityMetadata();
        SetMetadataProperty(entityMetadata, "LogicalName", logicalName);
        SetMetadataProperty(entityMetadata, "OwnershipType", ownership);
        SetMetadataProperty(entityMetadata, "PrimaryIdAttribute", logicalName + "id");

        // Set attributes via reflection (read-only property)
        SetMetadataProperty(entityMetadata, "Attributes", attributes);

        skeleton.EntityMetadata[logicalName] = entityMetadata;

        // Add default state/status (Active=0 -> Active=1)
        if (!skeleton.DefaultStateStatus.ContainsKey(logicalName))
        {
            skeleton.DefaultStateStatus[logicalName] = new Dictionary<int, int> { { 0, 1 } };
        }
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
    private static void ConvertSolutionExportWorkflows(string solutionDir, string workflowsDir)
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

    private static void CopyXmlFiles(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir)) return;

        foreach (var file in Directory.GetFiles(sourceDir, "*.xml"))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            // Don't overwrite — first solution's version wins (same as Merge behavior)
            if (!File.Exists(destFile))
            {
                File.Copy(file, destFile);
            }
        }
    }
}
