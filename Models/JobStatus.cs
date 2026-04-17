namespace DummyCicServer.Models;

// ── Polling Response ─────────────────────────────────────────────────

public sealed class PollingResponse
{
    public string RequestId { get; set; } = string.Empty;
    public string EventAlias { get; set; } = string.Empty;
    public string EimId { get; set; } = string.Empty;
    public string State { get; set; } = "open";
    public string AcceptedAt { get; set; } = string.Empty;
    public string ExpiresAt { get; set; } = string.Empty;
    public int LastSequence { get; set; }
    public bool HasMore { get; set; }
    public int? NextAfterSequence { get; set; }
    public List<PollingResponseItem> Items { get; set; } = new();
}

public sealed class PollingResponseItem
{
    public int Sequence { get; set; }
    public string EventAlias { get; set; } = string.Empty;
    public string ReceivedAt { get; set; } = string.Empty;
    public bool Terminal { get; set; }
    public Dictionary<string, object> Payload { get; set; } = new();
}
