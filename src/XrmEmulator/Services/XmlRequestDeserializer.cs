using System.Runtime.Serialization;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Xml;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata.Query;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk;
using System.Globalization;

namespace XrmEmulator.Services;

/// <summary>
/// Implementation of IXmlRequestDeserializer that deserializes SOAP XML into OrganizationRequest objects.
/// </summary>
internal sealed class XmlRequestDeserializer : IXmlRequestDeserializer
{
    private readonly ILogger<XmlRequestDeserializer> _logger;
    private readonly IRequestMapper _requestMapper;

    public XmlRequestDeserializer(ILogger<XmlRequestDeserializer> logger, IRequestMapper requestMapper)
    {
        _logger = logger;
        _requestMapper = requestMapper;
    }

    /// <summary>
    /// Deserialize request.
    /// </summary>
    /// <param name="soapXml">The SOAP XML string to deserialize.</param>
    /// <returns>An OrganizationRequest deserialized from the SOAP XML.</returns>
    public OrganizationRequest DeserializeRequest(string soapXml)
    {
        ArgumentNullException.ThrowIfNull(soapXml);

        _logger.LogDeserializingSoapXmlRequest();

        try
        {
            var doc = XDocument.Parse(soapXml);

            // Find the request element in the SOAP envelope
            var requestElement = FindRequestElement(doc);
            if (requestElement == null)
            {
                throw new InvalidOperationException("No request element found in SOAP XML");
            }

            // Extract the request name and type information
            var requestName = ExtractRequestName(requestElement);
            _logger.LogFoundRequestName(requestName);

            // Create the appropriate OrganizationRequest based on the request name
            var request = CreateTypedRequest(requestName, requestElement);

            _logger.LogSuccessfullyDeserializedType(request.GetType().Name);
            return request;
        }
        catch (Exception ex)
        {
            _logger.LogFailedToDeserializeSoapXmlRequest(ex);
            throw new InvalidOperationException("Failed to deserialize SOAP XML request", ex);
        }
    }

    private XElement? FindRequestElement(XDocument doc)
    {
        // Look for request element in various possible locations in the SOAP envelope
        // IMPORTANT: Order matters - we want the top-level request element (direct child of Execute),
        // not any nested request elements within OrganizationRequestCollection
        var possiblePaths = new[]
        {
            "//*[local-name()='Execute']/*[local-name()='request']",  // Most specific - direct child of Execute
            "//*[local-name()='Body']/*[local-name()='Execute']/*[local-name()='request']",  // Full path from Body
            "//*[local-name()='request' and ancestor::*[local-name()='Execute'] and not(ancestor::*[local-name()='OrganizationRequestCollection'])]",  // Request that's under Execute but not under OrganizationRequestCollection
            "//*[local-name()='request']",  // Fallback - any request element
            "//request"  // Least specific fallback
        };

        foreach (var path in possiblePaths)
        {
            try
            {
                var elements = doc.XPathSelectElements(path);
                var element = elements.FirstOrDefault();
                if (element != null)
                {
                    _logger.LogFoundRequestElementUsingPath(path);
                    return element;
                }
            }
            catch (Exception ex)
            {
                _logger.LogFailedToFindElementWithPath(path, ex.Message);
            }
        }

        return null;
    }

    private static string ExtractRequestName(XElement requestElement)
    {
        // Try to get request name from various sources
        // IMPORTANT: Check type attribute FIRST to avoid finding nested RequestName elements in ExecuteMultiple scenarios

        // 1. From type attribute (most reliable, avoids nested elements)
        var typeAttribute = requestElement.Attributes()
            .FirstOrDefault(a => string.Equals(a.Name.LocalName, "type", StringComparison.Ordinal));
        if (typeAttribute != null)
        {
            var typeValue = typeAttribute.Value;

            // Extract request name from type like "a:ExecuteMultipleRequest" or "b:WhoAmIRequest"
            var parts = typeValue.Split(':');
            if (parts.Length > 1)
            {
                var requestType = parts[1];
                if (requestType.EndsWith("Request", StringComparison.Ordinal))
                {
                    return requestType.Substring(0, requestType.Length - "Request".Length);
                }
            }
        }

        // 2. From RequestName element (direct child only, not all descendants to avoid nested requests)
        var requestNameElement = requestElement.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "RequestName", StringComparison.Ordinal));
        if (requestNameElement != null && !string.IsNullOrWhiteSpace(requestNameElement.Value))
        {
            return requestNameElement.Value;
        }

        // 3. From element name itself
        var elementName = requestElement.Name.LocalName;
        if (elementName.EndsWith("Request", StringComparison.Ordinal))
        {
            return elementName.Substring(0, elementName.Length - "Request".Length);
        }

        throw new InvalidOperationException($"Unable to determine request name from element: {requestElement.Name}");
    }

    private OrganizationRequest CreateTypedRequest(string requestName, XElement requestElement)
    {
        // Manual extraction is required because the Dataverse SOAP protocol uses a generic <request> element
        // with xsi:type attribute (e.g., <request i:type="a:ExecuteMultipleRequest">), which DataContractSerializer
        // cannot deserialize since it expects the element name to match the type name.
        var parameters = new ParameterCollection();
        ExtractParametersIntoCollection(parameters, requestElement);

        var request = _requestMapper.MapToTypedRequest(requestName, parameters);

        // Extract RequestId if present (direct child only, not nested ones)
        var requestIdElement = requestElement.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "RequestId", StringComparison.Ordinal));
        if (requestIdElement != null && Guid.TryParse(requestIdElement.Value, out var requestId))
        {
            request.RequestId = requestId;
        }

        return request;
    }

    private void ExtractParametersIntoCollection(ParameterCollection parameters, XElement requestElement)
    {
        // Look for Parameters element
        var parametersElement = requestElement.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Parameters", StringComparison.Ordinal));

        if (parametersElement != null)
        {
            // Extract key-value pairs from parameters (only direct children, not nested ones)
            var keyValuePairs = parametersElement.Elements()
                .Where(e => string.Equals(e.Name.LocalName, "KeyValuePairOfstringanyType", StringComparison.Ordinal) ||
                           e.Name.LocalName.Contains("KeyValuePair", StringComparison.Ordinal));

            foreach (var kvp in keyValuePairs)
            {
                var keyElement = kvp.Descendants()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "key", StringComparison.Ordinal));
                var valueElement = kvp.Descendants()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "value", StringComparison.Ordinal));

                if (keyElement != null && valueElement != null)
                {
                    var key = keyElement.Value;
                    var value = ConvertValue(valueElement);
                    parameters[key] = value;
                }
            }
        }
    }

    private object? ConvertValue(XElement valueElement)
    {
        // Get the type attribute to determine how to convert the value
        var typeAttribute = valueElement.Attributes()
            .FirstOrDefault(a => string.Equals(a.Name.LocalName, "type", StringComparison.Ordinal));

        if (typeAttribute != null)
        {
            var typeValue = typeAttribute.Value.ToLowerInvariant();

            if (typeValue.Contains("organizationrequestcollection", StringComparison.Ordinal))
            {
                return DeserializeOrganizationRequestCollection(valueElement);
            }
            else if (typeValue.Contains("executemultiplesettings", StringComparison.Ordinal))
            {
                return DeserializeExecuteMultipleSettings(valueElement);
            }
            else if (typeValue.Contains("entityreference", StringComparison.Ordinal))
            {
                return DeserializeEntityReference(valueElement);
            }
            else if (typeValue.Contains("entitycollection", StringComparison.Ordinal))
            {
                return DeserializeEntityCollection(valueElement);
            }
            else if (typeValue.Contains("optionsetvalue", StringComparison.Ordinal))
            {
                return DeserializeOptionSetValue(valueElement);
            }
            else if (typeValue.Contains("money", StringComparison.Ordinal))
            {
                return DeserializeMoney(valueElement);
            }
            else if (typeValue.Contains("columnset", StringComparison.Ordinal))
            {
                return DeserializeColumnSet(valueElement);
            }
            else if (typeValue.Contains("queryexpression", StringComparison.Ordinal))
            {
                return DeserializeQueryExpression(valueElement);
            }
            else if (typeValue.Contains("a:entity", StringComparison.Ordinal))
            {
                return DeserializeEntity(valueElement);
            }
            else if (typeValue.Contains("guid", StringComparison.Ordinal))
            {
                if (Guid.TryParse(valueElement.Value, out var guidValue))
                {
                    return guidValue;
                }
            }
            else if (typeValue.Contains("int", StringComparison.Ordinal))
            {
                if (int.TryParse(valueElement.Value, CultureInfo.InvariantCulture, out var intValue))
                {
                    return intValue;
                }
            }
            else if (typeValue.Contains("bool", StringComparison.Ordinal))
            {
                if (bool.TryParse(valueElement.Value, out var boolValue))
                {
                    return boolValue;
                }
            }
            else if (typeValue.Contains("datetime", StringComparison.Ordinal))
            {
                if (DateTime.TryParse(valueElement.Value, CultureInfo.InvariantCulture, out var dateValue))
                {
                    return dateValue;
                }
            }
            else
            {
                // Try to handle any other enum types generically
                var enumType = TryResolveEnumType(typeValue);
                if (enumType != null)
                {
                    try
                    {
                        // Special handling for flags enums which can have multiple space-separated values
                        var enumValue = valueElement.Value;
                        _logger.LogParsingEnumValue(enumType.Name, enumValue);

                        // Check if this is a flags enum
                        bool isFlagsEnum = enumType.GetCustomAttributes(typeof(FlagsAttribute), false).Length > 0;

                        // Handle multiple space-separated enum values for flags enums
                        if (isFlagsEnum && enumValue.Contains(' ', StringComparison.Ordinal))
                        {
                            var parts = enumValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            object combinedValue = Enum.ToObject(enumType, 0); // Start with 0 flags

                            foreach (var part in parts)
                            {
                                if (Enum.TryParse(enumType, part.Trim(), true, out var singleFlag))
                                {
                                    // Combine flags using bitwise OR
                                    var currentIntValue = Convert.ToInt64(combinedValue, CultureInfo.InvariantCulture);
                                    var singleFlagIntValue = Convert.ToInt64(singleFlag, CultureInfo.InvariantCulture);
                                    combinedValue = Enum.ToObject(enumType, currentIntValue | singleFlagIntValue);
                                }
                                else
                                {
                                    _logger.LogUnknownEnumValue(enumType.Name, part);
                                }
                            }

                            return combinedValue;
                        }

                        // Try parsing as single string value
                        if (Enum.TryParse(enumType, enumValue, true, out var parsedEnum))
                        {
                            return parsedEnum;
                        }

                        // If string parsing fails, try parsing as int
                        if (int.TryParse(enumValue, CultureInfo.InvariantCulture, out var intValue))
                        {
                            return Enum.ToObject(enumType, intValue);
                        }

                        // For other enum types, try standard parsing
                        return Enum.Parse(enumType, valueElement.Value, true);
                    }
                    catch (Exception enumEx)
                    {
                        _logger.LogFailedToParseEnumValue(enumEx, valueElement.Value, typeValue);
                    }
                }
            }
        }

        // Default to string
        return valueElement.Value;
    }

    private Type? TryResolveEnumType(string typeValue)
    {
        // Common enum types in Dataverse
        var knownEnums = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["entityfilters"] = typeof(EntityFilters),
            ["attributetypecode"] = typeof(AttributeTypeCode),
            ["entityqueryexpression"] = typeof(EntityQueryExpression),
            ["privilegedepth"] = typeof(PrivilegeDepth),
            ["accessrights"] = typeof(AccessRights),
            ["propagationownershipoptions"] = typeof(PropagationOwnershipOptions),
            ["targetfieldtype"] = typeof(TargetFieldType)
        };

        // Try to match against known enum types
        foreach (var kvp in knownEnums)
        {
            if (typeValue.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        // Try to resolve from loaded assemblies if it looks like an enum
        if (typeValue.Contains("enum", StringComparison.OrdinalIgnoreCase))
        {
            // Extract the type name from namespace-qualified names like "c:SomeEnumType"
            var typeName = typeValue.Contains(':', StringComparison.Ordinal) ? typeValue.Split(':').Last() : typeValue;

            // Search in Microsoft.Xrm.Sdk and Microsoft.Crm.Sdk assemblies
            var assemblies = new[]
            {
                typeof(EntityFilters).Assembly, // Microsoft.Xrm.Sdk
                typeof(WhoAmIRequest).Assembly // Microsoft.Crm.Sdk.Messages
            };

            foreach (var assembly in assemblies)
            {
                var type = assembly.GetTypes()
                    .FirstOrDefault(t => t.IsEnum &&
                        t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

                if (type != null)
                {
                    _logger.LogResolvedEnumType(type.FullName);
                    return type;
                }
            }
        }

        return null;
    }

    private Entity DeserializeEntity(XElement entityElement)
    {
        // Extract LogicalName (use Elements() not Descendants() to get direct children only)
        // IMPORTANT: Using Descendants() would find nested LogicalName elements from EntityReferences
        // within the entity's attributes, causing the wrong entity type to be detected
        var logicalNameElement = entityElement.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "LogicalName", StringComparison.Ordinal));
        if (logicalNameElement == null || string.IsNullOrWhiteSpace(logicalNameElement.Value))
        {
            throw new InvalidOperationException("Entity must have a LogicalName");
        }

        var logicalName = logicalNameElement.Value;

        // Extract Id if present (use Elements() not Descendants() to get direct children only)
        var idElement = entityElement.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Id", StringComparison.Ordinal));
        var entity = idElement != null && Guid.TryParse(idElement.Value, out var entityId) && entityId != Guid.Empty
            ? new Entity(logicalName, entityId)
            : new Entity(logicalName);

        // Extract Attributes (use Elements() to get direct children only)
        var attributesElement = entityElement.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Attributes", StringComparison.Ordinal));

        if (attributesElement != null)
        {
            // Extract key-value pairs from attributes (use Elements() not Descendants())
            // IMPORTANT: Using Descendants() would find nested KeyValuePairs from entities
            // within EntityCollection attributes, causing nested attributes to leak to parent
            var keyValuePairs = attributesElement.Elements()
                .Where(e => string.Equals(e.Name.LocalName, "KeyValuePairOfstringanyType", StringComparison.Ordinal) ||
                           e.Name.LocalName.Contains("KeyValuePair", StringComparison.Ordinal));

            foreach (var kvp in keyValuePairs)
            {
                var keyElement = kvp.Descendants()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "key", StringComparison.Ordinal));
                var valueElement = kvp.Descendants()
                    .FirstOrDefault(e => string.Equals(e.Name.LocalName, "value", StringComparison.Ordinal));

                if (keyElement != null && valueElement != null)
                {
                    var key = keyElement.Value;
                    var value = ConvertValue(valueElement);
                    entity[key] = value;
                }
            }
        }

        return entity;
    }

    private static EntityReference DeserializeEntityReference(XElement entityRefElement)
    {
        // Extract LogicalName (use Elements() not Descendants() to get direct children only)
        var logicalNameElement = entityRefElement.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "LogicalName", StringComparison.Ordinal));
        if (logicalNameElement == null || string.IsNullOrWhiteSpace(logicalNameElement.Value))
        {
            throw new InvalidOperationException("EntityReference must have a LogicalName");
        }

        var logicalName = logicalNameElement.Value;

        // Extract Id (use Elements() not Descendants() to get direct children only)
        var idElement = entityRefElement.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Id", StringComparison.Ordinal));
        if (idElement == null || !Guid.TryParse(idElement.Value, out var entityId) || entityId == Guid.Empty)
        {
            throw new InvalidOperationException("EntityReference must have a valid Id");
        }

        return new EntityReference(logicalName, entityId);
    }

    private EntityCollection DeserializeEntityCollection(XElement collectionElement)
    {
        var collection = new EntityCollection();

        // Find the Entities element
        var entitiesElement = collectionElement.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Entities", StringComparison.Ordinal));

        if (entitiesElement != null)
        {
            // Find all Entity elements within Entities
            var entityElements = entitiesElement.Elements()
                .Where(e => string.Equals(e.Name.LocalName, "Entity", StringComparison.Ordinal));

            foreach (var entityElement in entityElements)
            {
                var entity = DeserializeEntity(entityElement);
                collection.Entities.Add(entity);
            }
        }

        // Extract EntityName if present
        var entityNameElement = collectionElement.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "EntityName", StringComparison.Ordinal));
        if (entityNameElement != null && !string.IsNullOrWhiteSpace(entityNameElement.Value))
        {
            collection.EntityName = entityNameElement.Value;
        }

        return collection;
    }

    private static OptionSetValue DeserializeOptionSetValue(XElement optionSetElement)
    {
        // Extract Value element
        var valueElement = optionSetElement.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Value", StringComparison.Ordinal));

        if (valueElement != null && int.TryParse(valueElement.Value, CultureInfo.InvariantCulture, out var value))
        {
            return new OptionSetValue(value);
        }

        throw new InvalidOperationException("OptionSetValue must have a valid integer Value");
    }

    private static Money DeserializeMoney(XElement moneyElement)
    {
        // Extract Value element
        var valueElement = moneyElement.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Value", StringComparison.Ordinal));

        if (valueElement != null && decimal.TryParse(valueElement.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return new Money(value);
        }

        throw new InvalidOperationException("Money must have a valid decimal Value");
    }

    private static ColumnSet DeserializeColumnSet(XElement columnSetElement)
    {
        // Check if it's an "all columns" request
        var allColumnsElement = columnSetElement.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "AllColumns", StringComparison.Ordinal));
        if (allColumnsElement != null && bool.TryParse(allColumnsElement.Value, out var allColumns) && allColumns)
        {
            return new ColumnSet(true);
        }

        // Otherwise, extract specific columns
        var columnsElement = columnSetElement.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Columns", StringComparison.Ordinal));

        if (columnsElement != null)
        {
            var columnNames = new List<string>();

            // Extract column names from array
            var stringElements = columnsElement.Descendants()
                .Where(e => string.Equals(e.Name.LocalName, "string", StringComparison.Ordinal));

            foreach (var stringElement in stringElements)
            {
                if (!string.IsNullOrWhiteSpace(stringElement.Value))
                {
                    columnNames.Add(stringElement.Value);
                }
            }

            return new ColumnSet(columnNames.ToArray());
        }

        // Default to empty column set if no specific columns are found
        return new ColumnSet();
    }

    private static QueryExpression DeserializeQueryExpression(XElement queryElement)
    {
        // Use DataContractSerializer to deserialize the QueryExpression XML
        try
        {
            using var reader = queryElement.CreateReader();
            var serializer = new DataContractSerializer(typeof(QueryExpression));
            var query = (QueryExpression)serializer.ReadObject(reader)!;

            // Check if DataContractSerializer populated the EntityName correctly
            if (string.IsNullOrEmpty(query.EntityName))
            {
                // DataContractSerializer succeeded but didn't populate EntityName, fall back to manual
                return DeserializeQueryExpressionManually(queryElement);
            }

            return query;
        }
        catch (Exception)
        {
            // Fallback to manual deserialization if DataContractSerializer fails
            return DeserializeQueryExpressionManually(queryElement);
        }
    }

    private static QueryExpression DeserializeQueryExpressionManually(XElement queryElement)
    {
        var query = new QueryExpression();

        // Extract EntityName (use Elements() to get direct children, not all descendants)
        var entityNameElement = queryElement.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "EntityName", StringComparison.Ordinal));

        if (entityNameElement != null && !string.IsNullOrWhiteSpace(entityNameElement.Value))
        {
            query.EntityName = entityNameElement.Value;
        }

        // Extract ColumnSet (use Elements() for direct children)
        var columnSetElement = queryElement.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "ColumnSet", StringComparison.Ordinal));
        if (columnSetElement != null)
        {
            query.ColumnSet = DeserializeColumnSet(columnSetElement);
        }

        // Extract TopCount (use Elements() for direct children)
        var topCountElement = queryElement.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "TopCount", StringComparison.Ordinal));
        if (topCountElement != null && int.TryParse(topCountElement.Value, out var topCount))
        {
            query.TopCount = topCount;
        }

        // Extract Criteria (use Elements() for direct children)
        var criteriaElement = queryElement.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Criteria", StringComparison.Ordinal));
        if (criteriaElement != null)
        {
            query.Criteria = DeserializeFilterExpression(criteriaElement);
        }

        return query;
    }

    private static FilterExpression DeserializeFilterExpression(XElement filterElement)
    {
        var filter = new FilterExpression();

        // Extract FilterOperator
        var filterOperatorElement = filterElement.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "FilterOperator", StringComparison.Ordinal));
        if (filterOperatorElement != null && Enum.TryParse<LogicalOperator>(filterOperatorElement.Value, out var filterOperator))
        {
            filter.FilterOperator = filterOperator;
        }

        // Extract Conditions
        var conditionsElement = filterElement.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Conditions", StringComparison.Ordinal));
        if (conditionsElement != null)
        {
            var conditionElements = conditionsElement.Elements()
                .Where(e => string.Equals(e.Name.LocalName, "ConditionExpression", StringComparison.Ordinal));
            foreach (var conditionElement in conditionElements)
            {
                filter.Conditions.Add(DeserializeConditionExpression(conditionElement));
            }
        }

        return filter;
    }

    private static ConditionExpression DeserializeConditionExpression(XElement conditionElement)
    {
        var condition = new ConditionExpression();

        // Extract AttributeName
        var attributeNameElement = conditionElement.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "AttributeName", StringComparison.Ordinal));
        if (attributeNameElement != null)
        {
            condition.AttributeName = attributeNameElement.Value;
        }

        // Extract Operator
        var operatorElement = conditionElement.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Operator", StringComparison.Ordinal));
        if (operatorElement != null && Enum.TryParse<ConditionOperator>(operatorElement.Value, out var condOperator))
        {
            condition.Operator = condOperator;
        }

        // Extract Values
        var valuesElement = conditionElement.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Values", StringComparison.Ordinal));
        if (valuesElement != null)
        {
            var valueElements = valuesElement.Elements()
                .Where(e => string.Equals(e.Name.LocalName, "anyType", StringComparison.Ordinal) ||
                           string.Equals(e.Name.LocalName, "string", StringComparison.Ordinal));
            foreach (var valueElement in valueElements)
            {
                condition.Values.Add(valueElement.Value);
            }
        }

        return condition;
    }

    private OrganizationRequestCollection DeserializeOrganizationRequestCollection(XElement collectionElement)
    {
        var collection = new OrganizationRequestCollection();

        // Find all OrganizationRequest elements within the collection
        var requestElements = collectionElement.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "OrganizationRequest", StringComparison.Ordinal));

        foreach (var requestElement in requestElements)
        {
            // Each request element should have a RequestName
            var requestName = ExtractRequestName(requestElement);

            // Extract parameters
            var parameters = new ParameterCollection();
            ExtractParametersIntoCollection(parameters, requestElement);

            // Map to typed request
            var request = _requestMapper.MapToTypedRequest(requestName, parameters);

            // Extract RequestId if present
            var requestIdElement = requestElement.Descendants()
                .FirstOrDefault(e => string.Equals(e.Name.LocalName, "RequestId", StringComparison.Ordinal));
            if (requestIdElement != null && Guid.TryParse(requestIdElement.Value, out var requestId))
            {
                request.RequestId = requestId;
            }

            collection.Add(request);
        }

        return collection;
    }

    private static ExecuteMultipleSettings DeserializeExecuteMultipleSettings(XElement settingsElement)
    {
        var settings = new ExecuteMultipleSettings();

        // Extract ContinueOnError
        var continueOnErrorElement = settingsElement.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "ContinueOnError", StringComparison.Ordinal));
        if (continueOnErrorElement != null && bool.TryParse(continueOnErrorElement.Value, out var continueOnError))
        {
            settings.ContinueOnError = continueOnError;
        }

        // Extract ReturnResponses
        var returnResponsesElement = settingsElement.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, "ReturnResponses", StringComparison.Ordinal));
        if (returnResponsesElement != null && bool.TryParse(returnResponsesElement.Value, out var returnResponses))
        {
            settings.ReturnResponses = returnResponses;
        }

        return settings;
    }
}