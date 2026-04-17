namespace DummyCicServer.Models;

// ── Publish Request ──────────────────────────────────────────────────

public sealed class PublishRequest
{
    public string EventAlias { get; set; } = string.Empty;
    public string InteractionType { get; set; } = "fireAndForget";
    public string? Recipient { get; set; }
    public string? RequestId { get; set; }
    public PublishRequestEim? EventInteractionModel { get; set; }
    public Dictionary<string, object> Payload { get; set; } = new();
}

public sealed class PublishRequestEim
{
    public string EimId { get; set; } = string.Empty;
}

// ── Publish Response ─────────────────────────────────────────────────

public sealed class PublishResponse
{
    public string PublicationId { get; set; } = string.Empty;
    public string AcceptedAt { get; set; } = string.Empty;
    public string EventAlias { get; set; } = string.Empty;
    public string InteractionType { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public string? RequestState { get; set; }
    public string? ResponsePollPath { get; set; }
}
