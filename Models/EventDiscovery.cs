using System.Text.Json.Nodes;

namespace DummyCicServer.Models;

// ── Discovery ────────────────────────────────────────────────────────

public sealed class DiscoveryResponse
{
    public string DiscoveredAt { get; set; } = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
    public int PageSize { get; set; } = 50;
    public string? NextPageToken { get; set; }
    public List<EventAliasEntry> Items { get; set; } = new();
}

public sealed class EventAliasEntry
{
    public string EventAlias { get; set; } = string.Empty;
    public string BaseEventType { get; set; } = string.Empty;
    public EventAliasSchemaReference PublicationEventAliasSchema { get; set; } = new();
    public List<InteractionPattern> InteractionPatterns { get; set; } = new();
}

public sealed class EventAliasSchemaReference
{
    public string EventAliasSchemaId { get; set; } = string.Empty;
    public string EventAliasSchemaVersion { get; set; } = "1.0.0";
    public string EventAliasSchemaRole { get; set; } = "publication";
    public string EventAliasSchemaPath { get; set; } = string.Empty;
}

public sealed class InteractionPattern
{
    public string Type { get; set; } = "fireAndForget";
    public bool RecipientRequired { get; set; }
    public List<string>? AllowedRecipients { get; set; }
    public EventInteractionModel? EventInteractionModel { get; set; }
}

public sealed class EventInteractionModel
{
    public string EimId { get; set; } = string.Empty;
    public string InitiatingEventAlias { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 900;
    public List<ResponseTypeEntry> TerminalResponses { get; set; } = new();
    public List<ResponseTypeEntry>? NonTerminalResponses { get; set; }
    public bool MultipleResponsesAllowed { get; set; }
}

public sealed class ResponseTypeEntry
{
    public string EventAlias { get; set; } = string.Empty;
    public EventAliasSchemaReference ResponseEventAliasSchema { get; set; } = new();
}

// ── Schema Lookup ────────────────────────────────────────────────────

public sealed class EventAliasSchemaLookupResponse
{
    public string EventAliasSchemaId { get; set; } = string.Empty;
    public string EventAliasSchemaVersion { get; set; } = "1.0.0";
    public string EventAliasSchemaRole { get; set; } = "publication";
    public string EventAlias { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/schema+json";
    public JsonObject EventAliasSchema { get; set; } = new();
}

// ── Error ────────────────────────────────────────────────────────────

public sealed class ErrorResponse
{
    public ErrorDetail Error { get; set; } = new();
}

public sealed class ErrorDetail
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? RequestId { get; set; }
}
