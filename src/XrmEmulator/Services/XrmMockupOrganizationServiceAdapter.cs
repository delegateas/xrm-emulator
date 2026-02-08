using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using DG.Tools.XrmMockup;
using XrmEmulator.DataverseFakeApi.Models;

namespace XrmEmulator.DataverseFakeApi.Services;

/// <summary>
/// Implementation of IOrganizationServiceAdapter that uses XrmMockup as the backend
/// This provides realistic Dataverse behavior with proper business logic and data persistence
/// </summary>
public class XrmMockupOrganizationServiceAdapter : IOrganizationServiceAdapter, IDisposable
{
    private readonly IOrganizationService _organizationService;
    private readonly XrmMockupService _xrmMockupService;
    private readonly IDummyDataService _dummyDataService;
    private readonly ILogger<XrmMockupOrganizationServiceAdapter> _logger;
    private bool _disposed = false;

    public XrmMockupOrganizationServiceAdapter(
        IDummyDataService dummyDataService,
        ILogger<XrmMockupOrganizationServiceAdapter> logger)
    {
        _dummyDataService = dummyDataService;
        _logger = logger;

        _logger.LogInformation("Initializing XrmMockup service");

        try
        {
            // Initialize XrmMockup with minimal metadata (no external CRM needed)
            _xrmMockupService = XrmMockupService.CreateEmpty();
            
            // Configure basic entity metadata
            ConfigureEntityMetadata();
            
            // Initialize with test data
            InitializeTestData();

            _organizationService = _xrmMockupService.GetOrganizationService();
            
            _logger.LogInformation("XrmMockup service initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize XrmMockup service");
            throw;
        }
    }

    public async Task<Entity> CreateAsync(string entityName, Entity entity)
    {
        _logger.LogInformation("Creating entity {EntityName} with {AttributeCount} attributes using XrmMockup", 
            entityName, entity.Attributes.Count);

        try
        {
            // Use XrmMockup's Create operation
            var newId = _organizationService.Create(entity);
            
            // Retrieve the created entity to return it with all system fields
            var createdEntity = _organizationService.Retrieve(entityName, newId, new ColumnSet(true));
            
            _logger.LogInformation("Created entity {EntityName} with ID {Id} using XrmMockup", entityName, newId);
            
            return createdEntity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create entity {EntityName} using XrmMockup", entityName);
            throw;
        }
    }

    public async Task<Entity?> RetrieveAsync(string entityName, Guid id, ColumnSet columnSet)
    {
        _logger.LogInformation("Retrieving entity {EntityName} with ID {Id} using XrmMockup", entityName, id);

        try
        {
            var entity = _organizationService.Retrieve(entityName, id, columnSet);
            _logger.LogInformation("Retrieved entity {EntityName} with ID {Id} using XrmMockup", entityName, id);
            return entity;
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("does not exist") || ex.Message.Contains("was not found"))
            {
                _logger.LogInformation("Entity {EntityName} with ID {Id} not found in XrmMockup", entityName, id);
                return null;
            }
            
            _logger.LogError(ex, "Failed to retrieve entity {EntityName} with ID {Id} using XrmMockup", entityName, id);
            throw;
        }
    }

    public async Task UpdateAsync(string entityName, Guid id, Entity entity)
    {
        _logger.LogInformation("Updating entity {EntityName} with ID {Id} - {AttributeCount} attributes using XrmMockup", 
            entityName, id, entity.Attributes.Count);

        try
        {
            // Ensure the entity has the correct ID and logical name
            entity.Id = id;
            entity.LogicalName = entityName;
            
            _organizationService.Update(entity);
            
            _logger.LogInformation("Updated entity {EntityName} with ID {Id} using XrmMockup", entityName, id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update entity {EntityName} with ID {Id} using XrmMockup", entityName, id);
            throw;
        }
    }

    public async Task DeleteAsync(string entityName, Guid id)
    {
        _logger.LogInformation("Deleting entity {EntityName} with ID {Id} using XrmMockup", entityName, id);

        try
        {
            _organizationService.Delete(entityName, id);
            
            _logger.LogInformation("Deleted entity {EntityName} with ID {Id} using XrmMockup", entityName, id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete entity {EntityName} with ID {Id} using XrmMockup", entityName, id);
            throw;
        }
    }

    public async Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query)
    {
        _logger.LogInformation("RetrieveMultiple for entity {EntityName} using XrmMockup", query.EntityName);

        try
        {
            var entityCollection = _organizationService.RetrieveMultiple(query);
            
            _logger.LogInformation("RetrieveMultiple returned {Count} records for {EntityName} using XrmMockup", 
                entityCollection.Entities.Count, query.EntityName);
            
            return entityCollection;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve multiple entities for {EntityName} using XrmMockup", query.EntityName);
            throw;
        }
    }

    public async Task<OrganizationResponse> ExecuteAsync(OrganizationRequest request)
    {
        _logger.LogInformation("Executing request {RequestType} using XrmMockup", request.GetType().Name);

        try
        {
            // Special handling for RetrieveMultipleRequest to ensure Query parameter is properly typed
            if (request is Microsoft.Xrm.Sdk.Messages.RetrieveMultipleRequest retrieveMultipleRequest)
            {
                if (retrieveMultipleRequest.Query != null)
                {
                    var queryType = retrieveMultipleRequest.Query.GetType();
                    _logger.LogDebug("RetrieveMultipleRequest Query type: {QueryType}", queryType.FullName);

                    // If Query is QueryExpression, we can proceed
                    if (retrieveMultipleRequest.Query is QueryExpression queryExpression)
                    {
                        _logger.LogDebug("Query is QueryExpression for entity: {EntityName}", queryExpression.EntityName);
                        // Use the dedicated RetrieveMultiple method instead
                        var result = _organizationService.RetrieveMultiple(queryExpression);
                        var retrieveMultipleResponse = new Microsoft.Xrm.Sdk.Messages.RetrieveMultipleResponse();
                        retrieveMultipleResponse.Results["EntityCollection"] = result;
                        SetResponseName(retrieveMultipleResponse, request);
                        _logger.LogInformation("RetrieveMultiple completed via QueryExpression path");
                        return retrieveMultipleResponse;
                    }
                }
            }

            var response = _organizationService.Execute(request);

            // XrmMockup doesn't set ResponseName, but consumers expect it to be set
            // Set it based on the request type (e.g., "CreateRequest" -> "Create")
            SetResponseName(response, request);

            // Special handling for ExecuteMultiple - set ResponseName on inner responses
            if (response is ExecuteMultipleResponse executeMultipleResponse &&
                request is ExecuteMultipleRequest executeMultipleRequest)
            {
                for (int i = 0; i < executeMultipleResponse.Responses.Count; i++)
                {
                    var responseItem = executeMultipleResponse.Responses[i];
                    if (responseItem.Response != null && responseItem.RequestIndex < executeMultipleRequest.Requests.Count)
                    {
                        var innerRequest = executeMultipleRequest.Requests[responseItem.RequestIndex];
                        SetResponseName(responseItem.Response, innerRequest);
                    }
                }
            }

            // Special handling for RetrieveEntityRequest - debug the metadata that comes back
            if (request is Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest retrieveEntityRequest &&
                response is Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse retrieveEntityResponse)
            {
                var entityMetadata = retrieveEntityResponse.EntityMetadata;
                _logger.LogInformation("RetrieveEntityRequest for {LogicalName} - Metadata: LogicalName='{LogicalName}', SchemaName='{SchemaName}', PrimaryIdAttribute='{PrimaryIdAttribute}', PrimaryNameAttribute='{PrimaryNameAttribute}', DisplayName='{DisplayName}'",
                    retrieveEntityRequest.LogicalName,
                    entityMetadata.LogicalName ?? "null",
                    entityMetadata.SchemaName ?? "null",
                    entityMetadata.PrimaryIdAttribute ?? "null",
                    entityMetadata.PrimaryNameAttribute ?? "null",
                    entityMetadata.DisplayName?.UserLocalizedLabel?.Label ?? "null");
            }

            _logger.LogInformation("Executed request {RequestType} using XrmMockup", request.GetType().Name);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute request {RequestType} using XrmMockup", request.GetType().Name);
            throw;
        }
    }

    public async Task<Microsoft.Crm.Sdk.Messages.WhoAmIResponse> WhoAmIAsync()
    {
        _logger.LogInformation("Executing WhoAmI function using XrmMockup");

        try
        {
            var request = new WhoAmIRequest();
            var response = (Microsoft.Crm.Sdk.Messages.WhoAmIResponse)_organizationService.Execute(request);
            
            _logger.LogInformation("WhoAmI completed for UserId: {UserId} using XrmMockup", response.UserId);
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute WhoAmI using XrmMockup");
            throw;
        }
    }

    private void ConfigureEntityMetadata()
    {
        _logger.LogInformation("Configuring entity metadata in XrmMockup");

        try
        {
            // Add Account entity metadata
            var accountMetadata = new EntityMetadata
            {
                LogicalName = "account",
                SchemaName = "Account",
                DisplayName = new Label("Account", 1033),
                PrimaryIdAttribute = "accountid",
                PrimaryNameAttribute = "name"
            };

            // Add Contact entity metadata
            var contactMetadata = new EntityMetadata
            {
                LogicalName = "contact",
                SchemaName = "Contact",
                DisplayName = new Label("Contact", 1033),
                PrimaryIdAttribute = "contactid",
                PrimaryNameAttribute = "fullname"
            };

            // Add SystemUser entity metadata
            var systemUserMetadata = new EntityMetadata
            {
                LogicalName = "systemuser",
                SchemaName = "SystemUser", 
                DisplayName = new Label("User", 1033),
                PrimaryIdAttribute = "systemuserid",
                PrimaryNameAttribute = "fullname"
            };

            // Configure attributes for each entity
            ConfigureAccountAttributes(accountMetadata);
            ConfigureContactAttributes(contactMetadata);
            ConfigureSystemUserAttributes(systemUserMetadata);

            // Add the metadata to XrmMockup
            _xrmMockupService.AddEntityMetadata(accountMetadata);
            _xrmMockupService.AddEntityMetadata(contactMetadata);
            _xrmMockupService.AddEntityMetadata(systemUserMetadata);

            _logger.LogInformation("Entity metadata configured successfully in XrmMockup");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure entity metadata in XrmMockup");
            throw;
        }
    }

    private void ConfigureAccountAttributes(EntityMetadata entityMetadata)
    {
        var attributes = new List<AttributeMetadata>
        {
            new StringAttributeMetadata
            {
                LogicalName = "accountid",
                SchemaName = "AccountId",
                AttributeType = AttributeTypeCode.Uniqueidentifier,
                IsPrimaryId = true
            },
            new StringAttributeMetadata
            {
                LogicalName = "name",
                SchemaName = "Name",
                AttributeType = AttributeTypeCode.String,
                MaxLength = 160
            },
            new StringAttributeMetadata
            {
                LogicalName = "accountnumber",
                SchemaName = "AccountNumber",
                AttributeType = AttributeTypeCode.String,
                MaxLength = 20
            },
            new StringAttributeMetadata
            {
                LogicalName = "telephone1",
                SchemaName = "Telephone1",
                AttributeType = AttributeTypeCode.String,
                MaxLength = 50
            },
            new StringAttributeMetadata
            {
                LogicalName = "websiteurl",
                SchemaName = "WebSiteURL",
                AttributeType = AttributeTypeCode.String,
                MaxLength = 200
            },
            new StringAttributeMetadata
            {
                LogicalName = "industrycode",
                SchemaName = "IndustryCode",
                AttributeType = AttributeTypeCode.Picklist
            }
        };

        // Add system attributes
        AddSystemAttributes(attributes);
        
        entityMetadata.SetFieldValue("_attributes", attributes.ToArray());
    }

    private void ConfigureContactAttributes(EntityMetadata entityMetadata)
    {
        var attributes = new List<AttributeMetadata>
        {
            new StringAttributeMetadata
            {
                LogicalName = "contactid",
                SchemaName = "ContactId",
                AttributeType = AttributeTypeCode.Uniqueidentifier,
                IsPrimaryId = true
            },
            new StringAttributeMetadata
            {
                LogicalName = "firstname",
                SchemaName = "FirstName",
                AttributeType = AttributeTypeCode.String,
                MaxLength = 50
            },
            new StringAttributeMetadata
            {
                LogicalName = "lastname",
                SchemaName = "LastName",
                AttributeType = AttributeTypeCode.String,
                MaxLength = 50
            },
            new StringAttributeMetadata
            {
                LogicalName = "fullname",
                SchemaName = "FullName",
                AttributeType = AttributeTypeCode.String,
                MaxLength = 160
            },
            new StringAttributeMetadata
            {
                LogicalName = "emailaddress1",
                SchemaName = "EMailAddress1",
                AttributeType = AttributeTypeCode.String,
                MaxLength = 100
            },
            new StringAttributeMetadata
            {
                LogicalName = "telephone1",
                SchemaName = "Telephone1",
                AttributeType = AttributeTypeCode.String,
                MaxLength = 50
            }
        };

        // Add system attributes
        AddSystemAttributes(attributes);
        
        entityMetadata.SetFieldValue("_attributes", attributes.ToArray());
    }

    private void ConfigureSystemUserAttributes(EntityMetadata entityMetadata)
    {
        var attributes = new List<AttributeMetadata>
        {
            new StringAttributeMetadata
            {
                LogicalName = "systemuserid",
                SchemaName = "SystemUserId",
                AttributeType = AttributeTypeCode.Uniqueidentifier,
                IsPrimaryId = true
            },
            new StringAttributeMetadata
            {
                LogicalName = "firstname",
                SchemaName = "FirstName",
                AttributeType = AttributeTypeCode.String,
                MaxLength = 64
            },
            new StringAttributeMetadata
            {
                LogicalName = "lastname",
                SchemaName = "LastName",
                AttributeType = AttributeTypeCode.String,
                MaxLength = 64
            },
            new StringAttributeMetadata
            {
                LogicalName = "fullname",
                SchemaName = "FullName",
                AttributeType = AttributeTypeCode.String,
                MaxLength = 200
            },
            new StringAttributeMetadata
            {
                LogicalName = "domainname",
                SchemaName = "DomainName",
                AttributeType = AttributeTypeCode.String,
                MaxLength = 1024
            },
            new StringAttributeMetadata
            {
                LogicalName = "internalemailaddress",
                SchemaName = "InternalEMailAddress",
                AttributeType = AttributeTypeCode.String,
                MaxLength = 100
            }
        };

        // Add system attributes
        AddSystemAttributes(attributes);
        
        entityMetadata.SetFieldValue("_attributes", attributes.ToArray());
    }

    private void AddSystemAttributes(List<AttributeMetadata> attributes)
    {
        // Add common system attributes
        attributes.AddRange(new[]
        {
            new DateTimeAttributeMetadata
            {
                LogicalName = "createdon",
                SchemaName = "CreatedOn",
                AttributeType = AttributeTypeCode.DateTime
            },
            new DateTimeAttributeMetadata
            {
                LogicalName = "modifiedon",
                SchemaName = "ModifiedOn",
                AttributeType = AttributeTypeCode.DateTime
            },
            new BigIntAttributeMetadata
            {
                LogicalName = "versionnumber",
                SchemaName = "VersionNumber",
                AttributeType = AttributeTypeCode.BigInt
            },
            new StateAttributeMetadata
            {
                LogicalName = "statecode",
                SchemaName = "StateCode",
                AttributeType = AttributeTypeCode.State
            },
            new StatusAttributeMetadata
            {
                LogicalName = "statuscode",
                SchemaName = "StatusCode",
                AttributeType = AttributeTypeCode.Status
            }
        });
    }

    private void InitializeTestData()
    {
        _logger.LogInformation("Initializing test data in XrmMockup");

        try
        {
            // Create the default system user first (needed for ownership)
            var whoAmI = _dummyDataService.GetWhoAmI();
            var defaultUser = new Entity("systemuser", whoAmI.UserId)
            {
                ["firstname"] = "System",
                ["lastname"] = "Administrator", 
                ["fullname"] = "System Administrator",
                ["domainname"] = "DOMAIN\\admin",
                ["internalemailaddress"] = "admin@contoso.com"
            };
            
            _organizationService.Create(defaultUser);

            // Initialize accounts from dummy data
            var accounts = _dummyDataService.GetAccounts();
            foreach (var account in accounts)
            {
                var entity = new Entity("account", account.AccountId)
                {
                    ["name"] = account.Name,
                    ["accountnumber"] = account.AccountNumber,
                    ["telephone1"] = account.Telephone1,
                    ["websiteurl"] = account.WebsiteUrl,
                    ["industrycode"] = account.IndustryCode
                };
                
                _organizationService.Create(entity);
            }

            // Initialize contacts from dummy data  
            var contacts = _dummyDataService.GetContacts();
            foreach (var contact in contacts)
            {
                var entity = new Entity("contact", contact.ContactId)
                {
                    ["firstname"] = contact.FirstName,
                    ["lastname"] = contact.LastName,
                    ["fullname"] = contact.FullName,
                    ["emailaddress1"] = contact.EmailAddress1,
                    ["telephone1"] = contact.Telephone1
                };

                if (contact.ParentCustomerId.HasValue)
                {
                    entity["parentcustomerid"] = new EntityReference("account", contact.ParentCustomerId.Value);
                }
                
                _organizationService.Create(entity);
            }

            _logger.LogInformation("Test data initialized successfully in XrmMockup");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize test data in XrmMockup");
            throw;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _xrmMockupService?.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Sets the ResponseName property on a response based on the request type.
    /// XrmMockup doesn't set this property, but consumers expect it to match the Dataverse API behavior.
    /// </summary>
    private static void SetResponseName(OrganizationResponse response, OrganizationRequest request)
    {
        if (response == null || request == null)
        {
            return;
        }

        // Only set if not already set
        if (string.IsNullOrEmpty(response.ResponseName))
        {
            var requestTypeName = request.GetType().Name;
            if (requestTypeName.EndsWith("Request", StringComparison.Ordinal))
            {
                // Strip "Request" suffix (e.g., "CreateRequest" -> "Create")
                response.ResponseName = requestTypeName.Substring(0, requestTypeName.Length - "Request".Length);
            }
            else
            {
                // Fallback to full type name
                response.ResponseName = requestTypeName;
            }
        }
    }
}