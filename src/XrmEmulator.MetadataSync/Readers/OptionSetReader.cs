using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace XrmEmulator.MetadataSync.Readers;

public static class OptionSetReader
{
    public static OptionSetMetadataBase[] Read(IOrganizationService service)
    {
        var request = new RetrieveAllOptionSetsRequest();
        var response = (RetrieveAllOptionSetsResponse)service.Execute(request);
        return response.OptionSetMetadata;
    }
}
