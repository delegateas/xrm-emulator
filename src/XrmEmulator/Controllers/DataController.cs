using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace XrmEmulator.DataverseFakeApi.Controllers;

/// <summary>
/// Data debugging controller that displays all entities and records in simple HTML format.
/// </summary>
[ApiController]
[Route("debug")]
public sealed class DataController : ControllerBase
{
    private readonly IOrganizationServiceAsync _organizationService;
    private readonly ILogger<DataController> _logger;

    public DataController(
        IOrganizationServiceAsync organizationService,
        ILogger<DataController> logger)
    {
        _organizationService = organizationService;
        _logger = logger;
    }

    /// <summary>
    /// Display all data in the emulator as simple HTML tables.
    /// </summary>
    /// <returns>HTML page with all entities and their data.</returns>
    [HttpGet("data")]
    public async Task<IActionResult> GetAllData()
    {
        try
        {
            _logger.LogInformation("Rendering debug data page");

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine("<title>XRM Emulator Data</title>");
            html.AppendLine("<meta charset='utf-8'>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("<h1>XRM Emulator Data</h1>");
            html.AppendLine($"<p>Generated at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>");

            // Retrieve all entity metadata
            var retrieveAllEntitiesRequest = new RetrieveAllEntitiesRequest
            {
                EntityFilters = EntityFilters.Entity,
                RetrieveAsIfPublished = false
            };

            var retrieveAllEntitiesResponse = (RetrieveAllEntitiesResponse)await _organizationService
                .ExecuteAsync(retrieveAllEntitiesRequest)
                .ConfigureAwait(false);

            var entities = retrieveAllEntitiesResponse.EntityMetadata
                .Where(e => !string.IsNullOrEmpty(e.LogicalName))
                .OrderBy(e => e.LogicalName)
                .ToList();

            html.AppendLine("<h2>Table of Contents</h2>");
            html.AppendLine("<ul>");
            foreach (var entity in entities)
            {
                var recordCount = await GetRecordCount(entity.LogicalName!).ConfigureAwait(false);
                html.AppendLine($"<li><a href='#entity_{entity.LogicalName}'>{entity.LogicalName}</a> ({recordCount} records)</li>");
            }
            html.AppendLine("</ul>");

            // Render each entity's data
            foreach (var entity in entities)
            {
                await RenderEntityData(html, entity).ConfigureAwait(false);
            }

            html.AppendLine("</body>");
            html.AppendLine("</html>");

            return Content(html.ToString(), "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render debug data page");
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Display a specific entity's data.
    /// </summary>
    /// <param name="entityName">The logical name of the entity.</param>
    /// <returns>HTML page with the entity's data.</returns>
    [HttpGet("data/{entityName}")]
    public async Task<IActionResult> GetEntityData(string entityName)
    {
        try
        {
            _logger.LogInformation("Rendering debug data page for entity {EntityName}", entityName);

            // Retrieve entity metadata
            var retrieveEntityRequest = new RetrieveEntityRequest
            {
                LogicalName = entityName,
                EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
                RetrieveAsIfPublished = false
            };

            var retrieveEntityResponse = (RetrieveEntityResponse)await _organizationService
                .ExecuteAsync(retrieveEntityRequest)
                .ConfigureAwait(false);

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine($"<title>{entityName} - XRM Emulator Data</title>");
            html.AppendLine("<meta charset='utf-8'>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine($"<h1>{entityName}</h1>");
            html.AppendLine($"<p><a href='/debug/data'>Back to all entities</a></p>");
            html.AppendLine($"<p>Generated at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>");

            await RenderEntityData(html, retrieveEntityResponse.EntityMetadata).ConfigureAwait(false);

            html.AppendLine("</body>");
            html.AppendLine("</html>");

            return Content(html.ToString(), "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render debug data page for entity {EntityName}", entityName);
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Display a specific record's data.
    /// </summary>
    /// <param name="entityName">The logical name of the entity.</param>
    /// <param name="id">The ID of the record.</param>
    /// <returns>HTML page with the record's data.</returns>
    [HttpGet("data/{entityName}/{id}")]
    public async Task<IActionResult> GetRecordData(string entityName, Guid id)
    {
        try
        {
            _logger.LogInformation("Rendering debug data page for record {EntityName} {Id}", entityName, id);

            // Retrieve the record
            var entity = await _organizationService
                .RetrieveAsync(entityName, id, new ColumnSet(true))
                .ConfigureAwait(false);

            if (entity == null)
            {
                return NotFound($"Record {entityName} {id} not found");
            }

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine($"<title>{entityName} {id} - XRM Emulator Data</title>");
            html.AppendLine("<meta charset='utf-8'>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine($"<h1>{entityName} - {id}</h1>");
            html.AppendLine($"<p><a href='/debug/data/{entityName}'>Back to {entityName}</a> | <a href='/debug/data'>All entities</a></p>");
            html.AppendLine($"<p>Generated at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>");

            html.AppendLine("<h2>Attributes</h2>");
            html.AppendLine("<table border='1' cellpadding='5' cellspacing='0'>");
            html.AppendLine("<tr><th>Attribute</th><th>Value</th><th>Type</th></tr>");

            foreach (var attr in entity.Attributes.OrderBy(a => a.Key))
            {
                html.AppendLine("<tr>");
                html.AppendLine($"<td><strong>{attr.Key}</strong></td>");
                html.AppendLine($"<td>{FormatAttributeValue(attr.Value)}</td>");
                html.AppendLine($"<td>{attr.Value?.GetType().Name ?? "null"}</td>");
                html.AppendLine("</tr>");
            }

            html.AppendLine("</table>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            return Content(html.ToString(), "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render debug data page for record {EntityName} {Id}", entityName, id);
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    private async Task<int> GetRecordCount(string entityName)
    {
        try
        {
            var query = new QueryExpression(entityName)
            {
                ColumnSet = new ColumnSet(false),
                PageInfo = new PagingInfo
                {
                    Count = 5000,
                    PageNumber = 1
                }
            };

            var result = await _organizationService.RetrieveMultipleAsync(query).ConfigureAwait(false);
            return result.Entities.Count;
        }
        catch
        {
            return 0;
        }
    }

    private async Task RenderEntityData(StringBuilder html, EntityMetadata entityMetadata)
    {
        var entityName = entityMetadata.LogicalName!;

        html.AppendLine($"<h2 id='entity_{entityName}'>{entityName}</h2>");

        try
        {
            // Query all records for this entity
            var query = new QueryExpression(entityName)
            {
                ColumnSet = new ColumnSet(true),
                PageInfo = new PagingInfo
                {
                    Count = 5000,
                    PageNumber = 1
                }
            };

            var result = await _organizationService.RetrieveMultipleAsync(query).ConfigureAwait(false);

            if (result.Entities.Count == 0)
            {
                html.AppendLine("<p>No records found.</p>");
                return;
            }

            html.AppendLine($"<p>Found {result.Entities.Count} record(s)</p>");

            // Get all unique attribute names from all records
            var allAttributes = result.Entities
                .SelectMany(e => e.Attributes.Keys)
                .Distinct()
                .OrderBy(k => k)
                .ToList();

            // Render table
            html.AppendLine("<table border='1' cellpadding='5' cellspacing='0'>");

            // Header row
            html.AppendLine("<tr>");
            html.AppendLine("<th>View</th>");
            foreach (var attr in allAttributes)
            {
                html.AppendLine($"<th>{attr}</th>");
            }
            html.AppendLine("</tr>");

            // Data rows
            foreach (var entity in result.Entities)
            {
                html.AppendLine("<tr>");

                // Link to record detail
                html.AppendLine($"<td><a href='/debug/data/{entityName}/{entity.Id}'>View</a></td>");

                foreach (var attr in allAttributes)
                {
                    if (entity.Contains(attr))
                    {
                        var value = entity[attr];
                        html.AppendLine($"<td>{FormatAttributeValue(value)}</td>");
                    }
                    else
                    {
                        html.AppendLine("<td></td>");
                    }
                }

                html.AppendLine("</tr>");
            }

            html.AppendLine("</table>");
        }
        catch (Exception ex)
        {
            html.AppendLine($"<p style='color: red;'>Error loading data: {ex.Message}</p>");
            _logger.LogWarning(ex, "Failed to load data for entity {EntityName}", entityName);
        }
    }

    private static string FormatAttributeValue(object? value)
    {
        if (value == null)
        {
            return "<em>null</em>";
        }

        return value switch
        {
            EntityReference entityRef => $"{entityRef.LogicalName}: {entityRef.Id} ({entityRef.Name})",
            OptionSetValue optionSet => optionSet.Value.ToString(),
            Money money => money.Value.ToString("C"),
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss"),
            bool boolValue => boolValue.ToString(),
            Guid guidValue => guidValue.ToString(),
            _ => value.ToString() ?? string.Empty
        };
    }
}
