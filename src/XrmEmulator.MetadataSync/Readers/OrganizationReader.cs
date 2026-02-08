using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace XrmEmulator.MetadataSync.Readers;

public static class OrganizationReader
{
    public static (Entity Organization, Entity RootBusinessUnit, List<Entity> Currencies) Read(
        IOrganizationService service)
    {
        var organization = RetrieveOrganization(service);
        var rootBusinessUnit = RetrieveRootBusinessUnit(service);
        var currencies = RetrieveCurrencies(service);

        return (organization, rootBusinessUnit, currencies);
    }

    private static Entity RetrieveOrganization(IOrganizationService service)
    {
        var query = new QueryExpression("organization")
        {
            ColumnSet = new ColumnSet(true),
            TopCount = 1
        };

        var results = service.RetrieveMultiple(query);
        return results.Entities.FirstOrDefault()
            ?? throw new InvalidOperationException("No organization entity found.");
    }

    private static Entity RetrieveRootBusinessUnit(IOrganizationService service)
    {
        var query = new QueryExpression("businessunit")
        {
            ColumnSet = new ColumnSet(true),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("parentbusinessunitid", ConditionOperator.Null)
                }
            }
        };

        var results = service.RetrieveMultiple(query);
        return results.Entities.FirstOrDefault()
            ?? throw new InvalidOperationException("No root business unit found.");
    }

    private static List<Entity> RetrieveCurrencies(IOrganizationService service)
    {
        var query = new QueryExpression("transactioncurrency")
        {
            ColumnSet = new ColumnSet(true)
        };

        var results = service.RetrieveMultiple(query);
        return results.Entities.ToList();
    }
}
