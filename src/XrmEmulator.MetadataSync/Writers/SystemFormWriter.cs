using System.ServiceModel;
using System.Text.RegularExpressions;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using XrmEmulator.MetadataSync.Models;

namespace XrmEmulator.MetadataSync.Writers;

public static class SystemFormWriter
{
    private static readonly Regex GeneratedGuidPattern =
        new(@"\{generated\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static Guid Create(IOrganizationService service, SystemFormDefinition form,
        string entityLogicalName, string? solutionUniqueName)
    {
        // Replace {generated} placeholders with real GUIDs
        var formXml = GeneratedGuidPattern.Replace(form.FormXml,
            _ => $"{{{Guid.NewGuid()}}}");

        var entity = new Entity("systemform");
        entity["name"] = form.Name;
        entity["objecttypecode"] = entityLogicalName;
        entity["type"] = new OptionSetValue(form.FormType);
        entity["formxml"] = formXml;

        Guid id;
        try
        {
            id = service.Create(entity);
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            throw new InvalidOperationException(
                $"Failed to create systemform '{form.Name}' for entity '{entityLogicalName}': {ex.Detail.Message}", ex);
        }

        // Add to solution separately
        if (!string.IsNullOrEmpty(solutionUniqueName))
        {
            var addRequest = new AddSolutionComponentRequest
            {
                ComponentId = id,
                ComponentType = 60, // SystemForm
                SolutionUniqueName = solutionUniqueName
            };
            service.Execute(addRequest);
        }

        return id;
    }

    public static void Update(IOrganizationService service, SystemFormDefinition form)
    {
        var entity = new Entity("systemform", form.FormId);
        entity["name"] = form.Name;
        entity["formxml"] = form.FormXml;

        service.Update(entity);
    }

    public static void Delete(IOrganizationService service, Guid formId)
    {
        service.Delete("systemform", formId);
    }
}
