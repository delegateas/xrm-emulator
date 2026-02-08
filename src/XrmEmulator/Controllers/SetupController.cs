using System.Net;
using System.Text;
using DG.Tools.XrmMockup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace XrmEmulator.Controllers;

[ApiController]
[Route("debug/setup")]
public sealed class SetupController : ControllerBase
{
    private readonly XrmMockup365 _xrmMockup;
    private readonly IOrganizationServiceAsync _organizationService;
    private readonly ILogger<SetupController> _logger;

    public SetupController(
        XrmMockup365 xrmMockup,
        IOrganizationServiceAsync organizationService,
        ILogger<SetupController> logger)
    {
        _xrmMockup = xrmMockup;
        _organizationService = organizationService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetSetupPage()
    {
        try
        {
            var roles = await GetSecurityRoles().ConfigureAwait(false);
            var users = await GetExistingUsers().ConfigureAwait(false);
            var teams = await GetExistingTeams().ConfigureAwait(false);

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine("<title>XRM Emulator Setup</title>");
            html.AppendLine("<meta charset='utf-8'>");
            html.AppendLine("<style>");
            html.AppendLine("body { font-family: sans-serif; margin: 20px; }");
            html.AppendLine("table { border-collapse: collapse; margin: 10px 0; }");
            html.AppendLine("th, td { border: 1px solid #ccc; padding: 6px 10px; text-align: left; }");
            html.AppendLine("th { background: #f0f0f0; }");
            html.AppendLine("form { margin: 10px 0 30px 0; }");
            html.AppendLine("label { display: inline-block; min-width: 120px; }");
            html.AppendLine("input[type=text] { padding: 4px; margin: 4px 0; }");
            html.AppendLine(".field { margin: 6px 0; }");
            html.AppendLine(".roles { margin: 6px 0; max-height: 200px; overflow-y: auto; border: 1px solid #ccc; padding: 6px; }");
            html.AppendLine(".roles label { display: block; min-width: auto; }");
            html.AppendLine("button { padding: 6px 16px; margin-top: 8px; }");
            html.AppendLine("nav { margin-bottom: 20px; }");
            html.AppendLine("nav a { margin-right: 12px; }");
            html.AppendLine("</style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("<h1>XRM Emulator Setup</h1>");
            html.AppendLine("<nav><a href='/debug/data'>Data Browser</a> <a href='/debug/setup'>Setup</a></nav>");

            // System Users section
            html.AppendLine("<h2>System Users</h2>");
            if (users.Count > 0)
            {
                html.AppendLine("<table>");
                html.AppendLine("<tr><th>ID</th><th>Domain Name</th><th>First Name</th><th>Last Name</th><th>Business Unit</th></tr>");
                foreach (var user in users)
                {
                    html.AppendLine("<tr>");
                    html.AppendLine($"<td>{user.Id}</td>");
                    html.AppendLine($"<td>{Encode(user.GetAttributeValue<string>("domainname"))}</td>");
                    html.AppendLine($"<td>{Encode(user.GetAttributeValue<string>("firstname"))}</td>");
                    html.AppendLine($"<td>{Encode(user.GetAttributeValue<string>("lastname"))}</td>");
                    var buRef = user.GetAttributeValue<EntityReference>("businessunitid");
                    html.AppendLine($"<td>{Encode(buRef?.Name ?? buRef?.Id.ToString())}</td>");
                    html.AppendLine("</tr>");
                }
                html.AppendLine("</table>");
            }
            else
            {
                html.AppendLine("<p>No system users found.</p>");
            }

            html.AppendLine("<h3>Create System User</h3>");
            html.AppendLine("<form method='post' action='/debug/setup/users'>");
            html.AppendLine("<div class='field'><label>First Name</label><input type='text' name='firstname' required /></div>");
            html.AppendLine("<div class='field'><label>Last Name</label><input type='text' name='lastname' required /></div>");
            html.AppendLine("<div class='field'><label>Domain Name</label><input type='text' name='domainname' required /></div>");
            if (roles.Count > 0)
            {
                html.AppendLine("<div class='field'><label>Security Roles</label>");
                html.AppendLine("<div class='roles'>");
                foreach (var role in roles)
                {
                    var name = Encode(role.GetAttributeValue<string>("name"));
                    html.AppendLine($"<label><input type='checkbox' name='roles' value='{role.Id}' /> {name}</label>");
                }
                html.AppendLine("</div></div>");
            }
            html.AppendLine("<button type='submit'>Create User</button>");
            html.AppendLine("</form>");

            // Teams section
            html.AppendLine("<h2>Teams</h2>");
            if (teams.Count > 0)
            {
                html.AppendLine("<table>");
                html.AppendLine("<tr><th>ID</th><th>Name</th><th>Business Unit</th></tr>");
                foreach (var team in teams)
                {
                    html.AppendLine("<tr>");
                    html.AppendLine($"<td>{team.Id}</td>");
                    html.AppendLine($"<td>{Encode(team.GetAttributeValue<string>("name"))}</td>");
                    var buRef = team.GetAttributeValue<EntityReference>("businessunitid");
                    html.AppendLine($"<td>{Encode(buRef?.Name ?? buRef?.Id.ToString())}</td>");
                    html.AppendLine("</tr>");
                }
                html.AppendLine("</table>");
            }
            else
            {
                html.AppendLine("<p>No teams found.</p>");
            }

            html.AppendLine("<h3>Create Team</h3>");
            html.AppendLine("<form method='post' action='/debug/setup/teams'>");
            html.AppendLine("<div class='field'><label>Name</label><input type='text' name='name' required /></div>");
            if (roles.Count > 0)
            {
                html.AppendLine("<div class='field'><label>Security Roles</label>");
                html.AppendLine("<div class='roles'>");
                foreach (var role in roles)
                {
                    var name = Encode(role.GetAttributeValue<string>("name"));
                    html.AppendLine($"<label><input type='checkbox' name='roles' value='{role.Id}' /> {name}</label>");
                }
                html.AppendLine("</div></div>");
            }
            html.AppendLine("<button type='submit'>Create Team</button>");
            html.AppendLine("</form>");

            html.AppendLine("</body>");
            html.AppendLine("</html>");

            return Content(html.ToString(), "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render setup page");
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser()
    {
        try
        {
            var form = await Request.ReadFormAsync().ConfigureAwait(false);
            var firstname = form["firstname"].ToString();
            var lastname = form["lastname"].ToString();
            var domainname = form["domainname"].ToString();
            var roleIds = form["roles"]
                .Where(r => Guid.TryParse(r, out _))
                .Select(r => Guid.Parse(r!))
                .ToArray();

            var user = new Entity("systemuser")
            {
                ["firstname"] = firstname,
                ["lastname"] = lastname,
                ["domainname"] = domainname,
                ["businessunitid"] = _xrmMockup.RootBusinessUnit
            };

            var admin = _xrmMockup.GetAdminService();
            _xrmMockup.CreateUser(admin, user, roleIds);

            _logger.LogInformation("Created system user: {DomainName} ({FirstName} {LastName})",
                domainname, firstname, lastname);

            return Redirect("/debug/setup");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create system user");
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    [HttpPost("teams")]
    public async Task<IActionResult> CreateTeam()
    {
        try
        {
            var form = await Request.ReadFormAsync().ConfigureAwait(false);
            var name = form["name"].ToString();
            var roleIds = form["roles"]
                .Where(r => Guid.TryParse(r, out _))
                .Select(r => Guid.Parse(r!))
                .ToArray();

            var team = new Entity("team")
            {
                ["name"] = name,
                ["businessunitid"] = _xrmMockup.RootBusinessUnit
            };

            var admin = _xrmMockup.GetAdminService();
            _xrmMockup.CreateTeam(admin, team, roleIds);

            _logger.LogInformation("Created team: {Name}", name);

            return Redirect("/debug/setup");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create team");
            return StatusCode(500, $"Error: {ex.Message}");
        }
    }

    private async Task<List<Entity>> GetSecurityRoles()
    {
        try
        {
            var query = new QueryExpression("role")
            {
                ColumnSet = new ColumnSet("name", "roleid"),
                Orders = { new OrderExpression("name", OrderType.Ascending) },
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            var result = await _organizationService.RetrieveMultipleAsync(query).ConfigureAwait(false);
            return result.Entities.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve security roles");
            return [];
        }
    }

    private async Task<List<Entity>> GetExistingUsers()
    {
        try
        {
            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("firstname", "lastname", "domainname", "businessunitid"),
                Orders = { new OrderExpression("domainname", OrderType.Ascending) },
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            var result = await _organizationService.RetrieveMultipleAsync(query).ConfigureAwait(false);
            return result.Entities.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve system users");
            return [];
        }
    }

    private async Task<List<Entity>> GetExistingTeams()
    {
        try
        {
            var query = new QueryExpression("team")
            {
                ColumnSet = new ColumnSet("name", "businessunitid"),
                Orders = { new OrderExpression("name", OrderType.Ascending) },
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            var result = await _organizationService.RetrieveMultipleAsync(query).ConfigureAwait(false);
            return result.Entities.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve teams");
            return [];
        }
    }

    private static string Encode(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty);
}
