using System.ServiceModel;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace XrmEmulator.MetadataSync.Writers;

public static class DataImportWriter
{
    /// <summary>
    /// Upsert a single row: query by matchOn fields, update if found, create if not.
    /// Returns (id, wasCreated).
    /// Pass a shared <paramref name="lookupCache"/> across multiple calls to avoid redundant CRM queries
    /// for <c>lookup:tablename:fieldname</c> field types.
    /// </summary>
    public static (Guid Id, bool Created) UpsertRow(
        IOrganizationService service,
        string table,
        List<string> matchOn,
        Dictionary<string, string>? fieldTypes,
        Dictionary<string, JsonElement?> row,
        Dictionary<string, EntityReference>? lookupCache = null)
    {
        var existing = FindExisting(service, table, matchOn, fieldTypes, row, lookupCache);

        var entity = existing != null
            ? new Entity(table, existing.Value)
            : new Entity(table);

        foreach (var (key, jsonValue) in row)
        {
            if (jsonValue == null || jsonValue.Value.ValueKind == JsonValueKind.Null)
                continue;

            // On update, skip matchOn keys — the entity.Id already identifies the record,
            // and re-setting the primary key as a string-typed attribute causes a type
            // fault on Uniqueidentifier columns.
            if (existing != null && matchOn.Contains(key, StringComparer.OrdinalIgnoreCase))
                continue;

            var declaredType = fieldTypes?.GetValueOrDefault(key);
            var mapped = MapValue(key, jsonValue.Value, declaredType, service, lookupCache);
            if (mapped != null)
                entity[key] = mapped;
        }

        if (existing != null)
        {
            try
            {
                service.Update(entity);
                return (existing.Value, false);
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                var detail = FormatFault(ex.Detail);
                throw new InvalidOperationException(
                    $"Failed to update {table} ({existing.Value}):\n    {detail}", ex);
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                var inner = ex.InnerException;
                var chain = ex.Message;
                while (inner != null) { chain += $"\n    → {inner.Message}"; inner = inner.InnerException; }
                throw new InvalidOperationException(
                    $"Failed to update {table} ({existing.Value}):\n    {chain}", ex);
            }
        }
        else
        {
            try
            {
                var id = service.Create(entity);
                return (id, true);
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                var detail = FormatFault(ex.Detail);
                throw new InvalidOperationException(
                    $"Failed to create {table}:\n    {detail}", ex);
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                var inner = ex.InnerException;
                var chain = ex.Message;
                while (inner != null) { chain += $"\n    → {inner.Message}"; inner = inner.InnerException; }
                throw new InvalidOperationException(
                    $"Failed to create {table}:\n    {chain}", ex);
            }
        }
    }

    /// <summary>
    /// Walk the OrganizationServiceFault chain and return the full diagnostic string.
    /// Plugin exceptions typically surface in InnerFault or InnerFault.InnerFault.
    /// ErrorCode 0x80040265 (-2147220891) = InvalidPluginExecutionException.
    /// </summary>
    private static string FormatFault(OrganizationServiceFault fault)
    {
        var parts = new List<string>();
        var current = fault;
        var depth = 0;
        while (current != null)
        {
            var prefix = depth == 0 ? "" : $"[depth {depth}] ";
            var code = current.ErrorCode != 0 ? $" (0x{current.ErrorCode:X8})" : "";
            if (!string.IsNullOrWhiteSpace(current.Message))
                parts.Add($"{prefix}{current.Message.Trim()}{code}");
            current = current.InnerFault;
            depth++;
        }

        // Deduplicate adjacent identical messages
        var distinct = new List<string>();
        foreach (var p in parts)
            if (distinct.Count == 0 || distinct[^1] != p)
                distinct.Add(p);

        return distinct.Count == 0 ? "(no fault message)" : string.Join("\n    → ", distinct);
    }

    private static Guid? FindExisting(
        IOrganizationService service,
        string table,
        List<string> matchOn,
        Dictionary<string, string>? fieldTypes,
        Dictionary<string, JsonElement?> row,
        Dictionary<string, EntityReference>? lookupCache = null)
    {
        if (matchOn.Count == 0)
            return null;

        var query = new QueryExpression(table)
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 1,
            Criteria = new FilterExpression(LogicalOperator.And)
        };

        foreach (var field in matchOn)
        {
            if (!row.TryGetValue(field, out var jsonValue) || jsonValue == null || jsonValue.Value.ValueKind == JsonValueKind.Null)
            {
                query.Criteria.Conditions.Add(
                    new ConditionExpression(field, ConditionOperator.Null));
                continue;
            }

            var declaredType = fieldTypes?.GetValueOrDefault(field);

            // MultiSelect fields need ContainValues, not Equal
            if (declaredType == "multiselect")
            {
                var intValues = jsonValue.Value.GetString()!.Split(',')
                    .Select(p => (object)int.Parse(p.Trim()))
                    .ToArray();
                query.Criteria.Conditions.Add(
                    new ConditionExpression(field, ConditionOperator.ContainValues, intValues));
                continue;
            }

            // Lookup fields must be compared as Guid — a QueryExpression condition on a
            // Lookup column rejects the "table:guid" / display-name string forms. This is what
            // lets a child row be scoped to its parent, e.g. matching kf_partnerformline on
            // kf_partnerform + kf_name.
            if (declaredType != null
                && declaredType.StartsWith("lookup", StringComparison.OrdinalIgnoreCase))
            {
                var reference = (EntityReference)MapWithDeclaredType(
                    jsonValue.Value, declaredType, service, lookupCache);
                query.Criteria.Conditions.Add(
                    new ConditionExpression(field, ConditionOperator.Equal, reference.Id));
                continue;
            }

            var value = MapValueForCondition(jsonValue.Value, declaredType);
            query.Criteria.Conditions.Add(
                new ConditionExpression(field, ConditionOperator.Equal, value));
        }

        var result = service.RetrieveMultiple(query).Entities.FirstOrDefault();
        return result?.Id;
    }

    /// <summary>
    /// Map a JSON value to the appropriate SDK type for entity attributes.
    /// </summary>
    internal static object? MapValue(string key, JsonElement value, string? declaredType,
        IOrganizationService? service = null, Dictionary<string, EntityReference>? lookupCache = null)
    {
        // Explicit type override takes precedence
        if (declaredType != null)
            return MapWithDeclaredType(value, declaredType, service, lookupCache);

        return value.ValueKind switch
        {
            JsonValueKind.String => MapStringValue(value.GetString()!),
            JsonValueKind.Number => new OptionSetValue(value.GetInt32()), // Default: OptionSet
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.GetRawText()
        };
    }

    private static object MapWithDeclaredType(JsonElement value, string declaredType,
        IOrganizationService? service, Dictionary<string, EntityReference>? lookupCache)
    {
        // "lookup:tablename:fieldname" — resolve by querying CRM at commit time.
        // Example: "lookup:account:name" with value "Acme Corp" finds the account by name.
        if (declaredType.StartsWith("lookup:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = declaredType.Split(':', 3);
            if (parts.Length == 3)
            {
                if (service == null)
                    throw new InvalidOperationException(
                        $"Field type '{declaredType}' requires a CRM connection but none was provided.");
                return ResolveLookupByField(service, parts[1], parts[2], value.GetString()!, lookupCache);
            }
        }

        return declaredType.ToLowerInvariant() switch
        {
            "int" or "integer" => value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : int.Parse(value.GetString()!),
            "optionset" => new OptionSetValue(value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : int.Parse(value.GetString()!)),
            "decimal" => value.GetDecimal(),
            "money" => new Money(value.GetDecimal()),
            "string" => value.GetString()!,
            "bool" or "boolean" => value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => value.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true
            },
            "lookup" => ParseEntityReference(value.GetString()!),
            "multiselect" => ParseMultiSelect(value.GetString()!),
            _ => throw new InvalidOperationException($"Unknown field type: '{declaredType}'")
        };
    }

    private static EntityReference ResolveLookupByField(
        IOrganizationService service,
        string table,
        string matchField,
        string matchValue,
        Dictionary<string, EntityReference>? cache)
    {
        var cacheKey = $"{table}\x00{matchField}\x00{matchValue}";
        if (cache != null && cache.TryGetValue(cacheKey, out var cached))
            return cached;

        // Two rows are fetched, not one: a lookup that matches several records has no correct
        // answer, and picking the first silently points the import at an arbitrary record.
        var query = new QueryExpression(table)
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 2,
            Criteria = new FilterExpression
            {
                Conditions = { new ConditionExpression(matchField, ConditionOperator.Equal, matchValue) }
            }
        };
        var matches = service.RetrieveMultiple(query).Entities;
        if (matches.Count == 0)
            throw new InvalidOperationException(
                $"Cannot resolve lookup: no '{table}' record found where {matchField} = '{matchValue}'");
        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"Cannot resolve lookup: more than one '{table}' record has {matchField} = '{matchValue}' " +
                $"(e.g. {string.Join(", ", matches.Select(e => e.Id))}). " +
                $"Pick a field that is unique in the target environment, or reference the record by id " +
                $"via {{{{_pending/<file>#<key>}}}}.");

        var entityRef = new EntityReference(table, matches[0].Id);
        if (cache != null) cache[cacheKey] = entityRef;
        return entityRef;
    }

    /// <summary>
    /// Map a JSON value for use in QueryExpression conditions (primitive values only).
    /// </summary>
    private static object MapValueForCondition(JsonElement value, string? declaredType)
    {
        if (declaredType == "int" || declaredType == "integer")
            return value.ValueKind == JsonValueKind.Number ? value.GetInt32() : int.Parse(value.GetString()!);

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()!,
            JsonValueKind.Number => value.GetInt32(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => value.GetRawText()
        };
    }

    private static object MapStringValue(string value)
    {
        // EntityReference: "entityname:guid"
        if (value.Contains(':') && value.Split(':') is [var entityName, var guidStr]
            && Guid.TryParse(guidStr, out _))
        {
            return ParseEntityReference(value);
        }

        // MultiSelect OptionSet: comma-separated integers "100000000,100000001"
        if (value.Contains(',') && value.Split(',').All(p => int.TryParse(p.Trim(), out _)))
        {
            return ParseMultiSelect(value);
        }

        return value;
    }

    private static EntityReference ParseEntityReference(string value)
    {
        var parts = value.Split(':', 2);
        return new EntityReference(parts[0], Guid.Parse(parts[1]));
    }

    private static OptionSetValueCollection ParseMultiSelect(string value)
    {
        var values = value.Split(',')
            .Select(p => new OptionSetValue(int.Parse(p.Trim())))
            .ToArray();
        return new OptionSetValueCollection(values);
    }
}
