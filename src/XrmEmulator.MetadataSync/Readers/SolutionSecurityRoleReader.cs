using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace XrmEmulator.MetadataSync.Readers;

/// <summary>
/// Resolves which security roles belong to a solution.
/// </summary>
/// <remarks>
/// A role is only a solution component if someone explicitly added it, and the roles that carry
/// the privileges we grant are often *not* in the solution (out-of-the-box and org-wide custom
/// roles cannot be, or were never added). The audit therefore also needs the set of roles this
/// repository has actually modified, which comes from the committed/pending privilege files —
/// see <see cref="GetLocallyModifiedRoleNames"/>.
/// </remarks>
public static class SolutionSecurityRoleReader
{
    // Component type 20 = Role
    private const int SecurityRoleComponentType = 20;

    /// <summary>
    /// Role names that are components of the given solution.
    /// </summary>
    public static HashSet<string> GetSolutionRoleNames(IOrganizationService service, Guid solutionId)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (solutionId == Guid.Empty) return names;

        var query = new QueryExpression("solutioncomponent")
        {
            ColumnSet = new ColumnSet("objectid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("solutionid", ConditionOperator.Equal, solutionId),
                    new ConditionExpression("componenttype", ConditionOperator.Equal, SecurityRoleComponentType)
                }
            }
        };

        var roleIds = service.RetrieveMultiple(query).Entities
            .Select(c => c.GetAttributeValue<Guid>("objectid"))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        foreach (var chunk in roleIds.Chunk(200))
        {
            var roleQuery = new QueryExpression("role")
            {
                ColumnSet = new ColumnSet("name"),
                Criteria = new FilterExpression
                {
                    Conditions = { new ConditionExpression("roleid", ConditionOperator.In, chunk.Cast<object>().ToArray()) }
                }
            };

            foreach (var role in service.RetrieveMultiple(roleQuery).Entities)
            {
                var name = role.GetAttributeValue<string>("name");
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// Role names this repository has staged privilege changes for, read from the committed and
    /// pending privilege files under the environment directory. This is the practical audit
    /// surface: every role we have ever granted something to.
    /// </summary>
    public static HashSet<string> GetLocallyModifiedRoleNames(string baseDir)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var solutionExport = Path.Combine(baseDir, "SolutionExport");
        if (!Directory.Exists(solutionExport)) return names;

        var patterns = new[] { "*.securityrole.json", "*.securityroleprivremove.json" };
        foreach (var pattern in patterns)
        {
            foreach (var file in Directory.EnumerateFiles(solutionExport, pattern, SearchOption.AllDirectories))
            {
                var name = TryReadRoleName(file);
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name!);
            }
        }

        return names;
    }

    private static string? TryReadRoleName(string path)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!property.NameEquals("roleName") && !property.NameEquals("RoleName")) continue;
                return property.Value.GetString();
            }
        }
        catch
        {
            // A malformed or hand-edited file must not abort the audit.
        }

        return null;
    }
}
