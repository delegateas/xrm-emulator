using DG.Tools.XrmMockup;
using Microsoft.PowerPlatform.Dataverse.Client;
using XrmEmulator.Models.CrmMetadata;

namespace XrmEmulator.Services;

public class OrganizationServiceResolver
{
    private readonly IOrganizationServiceAsync _defaultService;
    private readonly XrmMockup365? _xrmMockup;

    public OrganizationServiceResolver(IOrganizationServiceAsync defaultService, XrmMockup365? xrmMockup = null)
    {
        _defaultService = defaultService;
        _xrmMockup = xrmMockup;
    }

    public IOrganizationServiceAsync Default => _defaultService;

    public IOrganizationServiceAsync GetForApp(CrmApp app)
    {
        return _defaultService;
    }

    /// <summary>
    /// Gets an organization service scoped to a specific form, for form-scoped business rule filtering.
    /// </summary>
    public IOrganizationServiceAsync GetForForm(Guid formId)
    {
        if (_xrmMockup == null) return _defaultService;
        return _xrmMockup.GetAdminService(new MockupServiceSettings(true, true, false, MockupServiceSettings.Role.SDK, formId));
    }
}
