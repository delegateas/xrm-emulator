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
    /// </summary>
    public static (Guid Id, bool Created) UpsertRow(
        IOrganizationService service,
        string table,
        List<string> matchOn,
        Dictionary<string, string>? fieldTypes,
        Dictionary<string, JsonElement?> row)
    {
        var existing = FindExisting(service, table, matchOn, fieldTypes, row);

        var entity = existing != null
            ? new Entity(table, existing.Value)
            : new Entity(table);

        foreach (var (key, jsonValue) in row)
        {
            if (jsonValue == null || jsonValue.Value.ValueKind == JsonValueKind.Null)
                continue;

            var declaredType = fieldTypes?.GetValueOrDefault(key);
            var mapped = MapValue(key, jsonValue.Value, declaredType);
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
                throw new InvalidOperationException(
                    $"Failed to update {table} ({existing.Value}): {ex.Detail.Message}", ex);
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"Failed to update {table} ({existing.Value}): {ex.Message}" +
                    (ex.InnerException != null ? $" → {ex.InnerException.Message}" : ""), ex);
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
                throw new InvalidOperationException(
                    $"Failed to create {table}: {ex.Detail.Message}", ex);
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"Failed to create {table}: {ex.Message}" +
                    (ex.InnerException != null ? $" → {ex.InnerException.Message}" : ""), ex);
            }
        }
    }

    private static Guid? FindExisting(
        IOrganizationService service,
        string table,
        List<string> matchOn,
        Dictionary<string, string>? fieldTypes,
        Dictionary<string, JsonElement?> row)
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
    internal static object? MapValue(string key, JsonElement value, string? declaredType)
    {
        // Explicit type override takes precedence
        if (declaredType != null)
            return MapWithDeclaredType(value, declaredType);

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

    private static object MapWithDeclaredType(JsonElement value, string declaredType)
    {
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
            "bool" or "boolean" => value.ValueKind == JsonValueKind.True || value.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            "lookup" => ParseEntityReference(value.GetString()!),
            "multiselect" => ParseMultiSelect(value.GetString()!),
            _ => throw new InvalidOperationException($"Unknown field type: '{declaredType}'")
        };
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
