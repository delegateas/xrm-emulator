using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class AssociationsImportWriter
{
    /// <summary>
    /// Resolves both sides of each pair by match-on attribute, then associates via
    /// <see cref="AssociateRequest"/>. Already-associated pairs are skipped.
    /// Lookup results are cached per side for the duration of the call so repeated
    /// matches on the same key don't re-query.
    /// </summary>
    public static ImportResult Apply(IOrganizationService service, AssociationsImportDefinition def, Action<string>? log = null)
    {
        var entity1Cache = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
        var entity2Cache = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);

        int created = 0;
        int skippedAlreadyAssociated = 0;
        int skippedUnresolved = 0;
        var unresolved = new List<string>();

        foreach (var pair in def.Pairs)
        {
            if (!pair.TryGetValue("entity1", out var v1) || !pair.TryGetValue("entity2", out var v2))
            {
                skippedUnresolved++;
                continue;
            }

            var id1 = ResolveCached(service, def.Entity1, v1, entity1Cache);
            var id2 = ResolveCached(service, def.Entity2, v2, entity2Cache);

            if (id1 is null)
            {
                skippedUnresolved++;
                unresolved.Add($"{def.Entity1.Table}.{def.Entity1.MatchOn}='{v1}'");
                continue;
            }
            if (id2 is null)
            {
                skippedUnresolved++;
                unresolved.Add($"{def.Entity2.Table}.{def.Entity2.MatchOn}='{v2}'");
                continue;
            }

            try
            {
                service.Execute(new AssociateRequest
                {
                    Target = new EntityReference(def.Entity1.Table, id1.Value),
                    RelatedEntities = new EntityReferenceCollection
                    {
                        new EntityReference(def.Entity2.Table, id2.Value),
                    },
                    Relationship = new Relationship(def.Relationship),
                });
                created++;
            }
            catch (Exception ex) when (ex.Message.Contains("Cannot insert duplicate key", StringComparison.OrdinalIgnoreCase)
                                    || ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                                    || ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
            {
                skippedAlreadyAssociated++;
            }
        }

        log?.Invoke($"Associations: {created} created, {skippedAlreadyAssociated} already existed, {skippedUnresolved} unresolved.");
        if (unresolved.Count > 0)
        {
            var preview = string.Join(", ", unresolved.Take(5));
            log?.Invoke($"  Unresolved samples: {preview}{(unresolved.Count > 5 ? "…" : "")}");
        }

        return new ImportResult(created, skippedAlreadyAssociated, skippedUnresolved);
    }

    private static Guid? ResolveCached(IOrganizationService service, AssociationSide side, string value, Dictionary<string, Guid?> cache)
    {
        if (cache.TryGetValue(value, out var cached)) return cached;

        var query = new QueryExpression(side.Table)
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 1,
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.Conditions.Add(new ConditionExpression(side.MatchOn, ConditionOperator.Equal, value));

        var result = service.RetrieveMultiple(query).Entities.FirstOrDefault();
        var id = result?.Id;
        cache[value] = id;
        return id;
    }

    public record ImportResult(int Created, int AlreadyExisted, int Unresolved);
}
