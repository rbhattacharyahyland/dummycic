namespace DummyCicServer.Models;

public sealed class JobStatusResponse
{
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = "InProgress";
    public string? CorrelationId { get; set; }
    public Dictionary<string, object>? ResponsePayload { get; set; }
    public string? FailureReason { get; set; }
}
