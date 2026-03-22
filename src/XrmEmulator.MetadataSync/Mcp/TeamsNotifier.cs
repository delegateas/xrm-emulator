using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace XrmEmulator.MetadataSync.Mcp;

public class TeamsNotifier
{
    private readonly string _clientId;
    private readonly string _tenantId;
    private string _refreshToken;
    private string? _accessToken;
    private readonly HttpClient _http = new();

    public TeamsNotifier(string clientId, string tenantId, string refreshToken)
    {
        _clientId = clientId;
        _tenantId = tenantId;
        _refreshToken = refreshToken;
    }

    public string CurrentRefreshToken => _refreshToken;

    /// <summary>
    /// Send an approval adaptive card to the approver via Teams 1:1 chat.
    /// </summary>
    public async Task SendApprovalCardAsync(
        ApprovalRequest approval,
        string approverEmail,
        string callbackBaseUrl,
        string hmacSigningKey)
    {
        await EnsureAccessTokenAsync();

        // Create or find 1:1 chat with approver
        var chatId = await CreateOneOnOneChatAsync(approverEmail);

        // Build adaptive card
        var card = BuildApprovalCard(approval, callbackBaseUrl, hmacSigningKey);

        // Send the card as a chat message
        await SendCardMessageAsync(chatId, card);
    }

    private async Task EnsureAccessTokenAsync()
    {
        if (_accessToken != null) return;

        var (accessToken, refreshToken) = await GraphAuthHelper.RefreshAccessTokenAsync(
            _clientId, _tenantId, _refreshToken);
        _accessToken = accessToken;
        _refreshToken = refreshToken;
    }

    private async Task<string> CreateOneOnOneChatAsync(string approverEmail)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/chats");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var body = new
        {
            chatType = "oneOnOne",
            members = new[]
            {
                new
                {
                    @type = "#microsoft.graph.aadUserConversationMember",
                    roles = new[] { "owner" },
                    user = new { id = "placeholder" },
                    // Use UPN format for the member
                    additionalData = new Dictionary<string, object>
                    {
                        ["user@odata.bind"] = $"https://graph.microsoft.com/v1.0/users('{approverEmail}')"
                    }
                }
            }
        };

        // Build the request body manually for correct OData binding
        var chatBody = $$"""
        {
            "chatType": "oneOnOne",
            "members": [
                {
                    "@odata.type": "#microsoft.graph.aadUserConversationMember",
                    "roles": ["owner"],
                    "user@odata.bind": "https://graph.microsoft.com/v1.0/users('{{approverEmail}}')"
                }
            ]
        }
        """;

        request.Content = new StringContent(chatBody, Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);
        var responseJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Failed to create Teams chat: {responseJson}");

        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private async Task SendCardMessageAsync(string chatId, string cardJson)
    {
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://graph.microsoft.com/v1.0/chats/{chatId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var messageBody = $$"""
        {
            "body": {
                "contentType": "html",
                "content": "<attachment id=\"approval-card\"></attachment>"
            },
            "attachments": [
                {
                    "id": "approval-card",
                    "contentType": "application/vnd.microsoft.card.adaptive",
                    "content": {{cardJson}}
                }
            ]
        }
        """;

        request.Content = new StringContent(messageBody, Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var errorJson = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Failed to send Teams message: {errorJson}");
        }
    }

    private static string BuildApprovalCard(
        ApprovalRequest approval,
        string callbackBaseUrl,
        string hmacSigningKey)
    {
        var itemLines = new StringBuilder();
        foreach (var item in approval.Items)
        {
            itemLines.Append($$"""
            ,{
                "type": "TextBlock",
                "text": "• {{JsonEncodedText.Encode(item.Type).ToString()}}: {{JsonEncodedText.Encode(item.DisplayName).ToString()}}",
                "wrap": true,
                "size": "Small"
            }
            """);
        }

        var approveToken = HmacHelper.ComputeHmac(hmacSigningKey, $"{approval.ApprovalId}:approve");
        var rejectToken = HmacHelper.ComputeHmac(hmacSigningKey, $"{approval.ApprovalId}:reject");

        var approveUrl = $"{callbackBaseUrl}/approve/{approval.ApprovalId}?token={Uri.EscapeDataString(approveToken)}";
        var rejectUrl = $"{callbackBaseUrl}/reject/{approval.ApprovalId}?token={Uri.EscapeDataString(rejectToken)}";

        var card = $$"""
        {
            "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
            "type": "AdaptiveCard",
            "version": "1.4",
            "body": [
                {
                    "type": "TextBlock",
                    "text": "MetadataSync Approval Request",
                    "weight": "Bolder",
                    "size": "Large"
                },
                {
                    "type": "FactSet",
                    "facts": [
                        { "title": "Approval ID", "value": "{{approval.ApprovalId}}" },
                        { "title": "Items", "value": "{{approval.Items.Count}} pending change(s)" },
                        { "title": "Requested", "value": "{{approval.CreatedAt:u}}" }
                    ]
                },
                {
                    "type": "TextBlock",
                    "text": "Pending Changes:",
                    "weight": "Bolder",
                    "spacing": "Medium"
                }
                {{itemLines}}
            ],
            "actions": [
                {
                    "type": "Action.OpenUrl",
                    "title": "✅ Approve",
                    "url": "{{approveUrl}}",
                    "style": "positive"
                },
                {
                    "type": "Action.OpenUrl",
                    "title": "❌ Reject",
                    "url": "{{rejectUrl}}",
                    "style": "destructive"
                }
            ]
        }
        """;

        // Return as a JSON string value (the card JSON is embedded as a string in the message)
        return JsonSerializer.Serialize(card);
    }
}
