using System.Collections.Concurrent;
using DummyCicServer.Models;

namespace DummyCicServer.Services;

/// <summary>
/// In-memory store that holds seed event schemas and tracks fired jobs.
/// Jobs auto-complete after a configurable number of poll attempts.
/// </summary>
public sealed class EventStore
{
    private readonly List<EventAlias> _eventAliases;
    private readonly ConcurrentDictionary<string, JobState> _jobs = new();

    /// <summary>Number of poll attempts before a job transitions to Completed.</summary>
    private const int PollsBeforeComplete = 3;

    public EventStore()
    {
        // Seed two sample events matching the spec examples.
        _eventAliases = new List<EventAlias>
        {
            new EventAlias
            {
                AliasId = "trigger-imr-automate-id",
                AliasName = "trigger-imr-automate",
                RequestFields = new List<RequestField>
                {
                    new() { FieldName = "documentId", FieldType = "string", IsFilterAttribute = false, Required = true },
                    new() { FieldName = "PO_Number", FieldType = "string", IsFilterAttribute = false, Required = false },
                    new() { FieldName = "Vendor_Name", FieldType = "string", IsFilterAttribute = false, Required = false }
                },
                ResponseFields = new List<ResponseField>
                {
                    new() { FieldName = "documentIds", FieldType = "stringlist" },
                    new() { FieldName = "extractedTotal", FieldType = "string" },
                    new() { FieldName = "vendorName", FieldType = "string" }
                },
                Pattern = new PatternInfo
                {
                    Pattern = "requestReply",
                    SubscriberCardinality = "single",
                    RecipientRequired = true,
                    AllowedRecipients = new List<string>
                    {
                        "my-automate-imr-process-named-0",
                        "my-automate-imr-process-named-1"
                    }
                }
            },
            new EventAlias
            {
                AliasId = "trigger-some-automate-id",
                AliasName = "trigger-some-automate",
                RequestFields = new List<RequestField>
                {
                    new() { FieldName = "patientType", FieldType = "string", IsFilterAttribute = true, Required = false }
                },
                ResponseFields = new List<ResponseField>
                {
                    new() { FieldName = "documentIds", FieldType = "stringlist" }
                },
                Pattern = new PatternInfo
                {
                    Pattern = "fireAndForget",
                    SubscriberCardinality = "multiple",
                    RecipientRequired = false
                }
            },
            new EventAlias
            {
                AliasId = "invoice-extraction-id",
                AliasName = "invoice-data-extraction",
                RequestFields = new List<RequestField>
                {
                    new() { FieldName = "Invoice_Document", FieldType = "document", IsFilterAttribute = false, Required = true },
                    new() { FieldName = "PO_Number", FieldType = "string", IsFilterAttribute = false, Required = false },
                    new() { FieldName = "Vendor_Name", FieldType = "string", IsFilterAttribute = false, Required = false }
                },
                ResponseFields = new List<ResponseField>
                {
                    new() { FieldName = "InvoiceTotal", FieldType = "string" },
                    new() { FieldName = "DueDate", FieldType = "string" },
                    new() { FieldName = "VendorName", FieldType = "string" }
                },
                Pattern = new PatternInfo
                {
                    Pattern = "requestReply",
                    SubscriberCardinality = "single",
                    RecipientRequired = false,
                    AllowedRecipients = new List<string>
                    {
                        "invoice-processor-east",
                        "invoice-processor-west"
                    }
                }
            }
        };
    }

    public EventDiscoveryResponse Discover(string eventType) =>
        new()
        {
            EventType = eventType,
            EventAliases = _eventAliases
        };

    public EventAlias? FindAlias(string aliasId) =>
        _eventAliases.FirstOrDefault(a =>
            a.AliasId.Equals(aliasId, StringComparison.OrdinalIgnoreCase));

    public string CreateJob(string aliasId, string? correlationId, Dictionary<string, object> payload, int? ttlSeconds)
    {
        var jobId = Guid.NewGuid().ToString();
        var alias = FindAlias(aliasId);
        var job = new JobState
        {
            JobId = jobId,
            AliasId = aliasId,
            CorrelationId = correlationId,
            Payload = payload,
            TtlSeconds = ttlSeconds ?? 600,
            CreatedAt = DateTimeOffset.UtcNow,
            PollCount = 0,
            IsFireAndForget = alias?.Pattern.Pattern == "fireAndForget"
        };
        _jobs[jobId] = job;
        return jobId;
    }

    public JobStatusResponse? PollJob(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
            return null;

        job.PollCount++;

        if (job.PollCount >= PollsBeforeComplete)
        {
            // Simulate completion with dummy response payload
            return new JobStatusResponse
            {
                JobId = jobId,
                Status = "Completed",
                CorrelationId = job.CorrelationId,
                ResponsePayload = new Dictionary<string, object>
                {
                    ["InvoiceTotal"] = "$12,450.00",
                    ["DueDate"] = "04/15/2026",
                    ["VendorName"] = "Contoso Ltd.",
                    ["documentIds"] = new[] { "8349015", "8349016" }
                }
            };
        }

        return new JobStatusResponse
        {
            JobId = jobId,
            Status = "InProgress",
            CorrelationId = job.CorrelationId
        };
    }

    public bool DeleteJob(string jobId) => _jobs.TryRemove(jobId, out _);

    private sealed class JobState
    {
        public string JobId { get; set; } = string.Empty;
        public string AliasId { get; set; } = string.Empty;
        public string? CorrelationId { get; set; }
        public Dictionary<string, object> Payload { get; set; } = new();
        public int TtlSeconds { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int PollCount { get; set; }
        public bool IsFireAndForget { get; set; }
    }
}
