namespace DummyCicServer.Models;

public sealed class FireEventRequest
{
    public string EventType { get; set; } = string.Empty;
    public string? Recipient { get; set; }
    public Dictionary<string, object> Payload { get; set; } = new();
}

public sealed class RequestReplyResponse
{
    public string Status { get; set; } = "accepted";
    public string JobId { get; set; } = string.Empty;
    public string IntegrationId { get; set; } = "integrationA";
    public int TtlSeconds { get; set; } = 600;
    public string ExpiresAt { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
}

public sealed class FireAndForgetResponse
{
    public string Status { get; set; } = "accepted";
    public string IntegrationId { get; set; } = "integrationA";
    public string? CorrelationId { get; set; }
}
