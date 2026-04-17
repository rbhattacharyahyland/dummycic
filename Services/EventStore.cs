using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DummyCicServer.Models;

namespace DummyCicServer.Services;

/// <summary>
/// In-memory store that holds seed event alias discovery data, JSON schemas,
/// and tracks published request/reply lifecycles.
/// Lifecycles auto-complete after a configurable number of poll attempts.
/// </summary>
public sealed class EventStore
{
    private readonly List<EventAliasEntry> _eventAliases;
    private readonly Dictionary<string, EventAliasSchemaLookupResponse> _schemas = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, RequestLifecycle> _requests = new();

    /// <summary>Number of poll attempts before a lifecycle transitions to completed.</summary>
    private const int PollsBeforeComplete = 3;

    public EventStore()
    {
        // ── Seed schemas ─────────────────────────────────────────────

        // Publication schema: trigger-imr-automate
        RegisterSchema("trigger-imr-automate.v1-payload", "1.0.0", "publication", "trigger-imr-automate.v1",
            MakeSchema(new Dictionary<string, (string type, string? format)>
            {
                ["documentId"] = ("string", "document-identifier"),
                ["PO_Number"] = ("string", null),
                ["Vendor_Name"] = ("string", null)
            }, required: new[] { "documentId" }));

        // Response schemas for trigger-imr-automate
        RegisterSchema("imr.completed.v1-payload", "1.0.0", "response", "imr.completed.v1",
            MakeSchema(new Dictionary<string, (string type, string? format)>
            {
                ["extractedTotal"] = ("string", null),
                ["vendorName"] = ("string", null),
                ["documentIds"] = ("string", null)
            }));

        RegisterSchema("imr.failed.v1-payload", "1.0.0", "response", "imr.failed.v1",
            MakeSchema(new Dictionary<string, (string type, string? format)>
            {
                ["errorMessage"] = ("string", null)
            }, required: new[] { "errorMessage" }));

        RegisterSchema("imr.progress.v1-payload", "1.0.0", "response", "imr.progress.v1",
            MakeSchema(new Dictionary<string, (string type, string? format)>
            {
                ["percentComplete"] = ("integer", null),
                ["stage"] = ("string", null)
            }));

        // Publication schema: trigger-some-automate (fire-and-forget only)
        RegisterSchema("trigger-some-automate.v1-payload", "1.0.0", "publication", "trigger-some-automate.v1",
            MakeSchema(new Dictionary<string, (string type, string? format)>
            {
                ["patientType"] = ("string", null)
            }));

        // Publication schema: invoice-data-extraction
        RegisterSchema("invoice-data-extraction.v1-payload", "1.0.0", "publication", "invoice-data-extraction.v1",
            MakeSchema(new Dictionary<string, (string type, string? format)>
            {
                ["Invoice_Document"] = ("string", "document-identifier"),
                ["PO_Number"] = ("string", null),
                ["Vendor_Name"] = ("string", null)
            }, required: new[] { "Invoice_Document" }));

        // Response schemas for invoice-data-extraction
        RegisterSchema("invoice.extracted.v1-payload", "1.0.0", "response", "invoice.extracted.v1",
            MakeSchema(new Dictionary<string, (string type, string? format)>
            {
                ["InvoiceTotal"] = ("string", null),
                ["DueDate"] = ("string", "date"),
                ["VendorName"] = ("string", null)
            }));

        RegisterSchema("invoice.rejected.v1-payload", "1.0.0", "response", "invoice.rejected.v1",
            MakeSchema(new Dictionary<string, (string type, string? format)>
            {
                ["rejectionReason"] = ("string", null)
            }, required: new[] { "rejectionReason" }));

        // ── All-types event: exercises every field type our UI supports ──

        // Publication schema: all-types-demo (covers 8 field types)
        RegisterSchema("all-types-demo.v1-payload", "1.0.0", "publication", "all-types-demo.v1",
            MakeSchemaAdvanced(new Dictionary<string, JsonObject>
            {
                // 1. string
                ["PatientName"] = new JsonObject { ["type"] = "string" },
                // 2. stringList (array of strings)
                ["DiagnosisCodes"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" }
                },
                // 3. enumString (string with enum constraint)
                ["Priority"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { "Low", "Medium", "High", "Critical" }
                },
                // 4. dateTime (string with date-time format)
                ["EncounterDate"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
                // 5. boolean
                ["IsUrgent"] = new JsonObject { ["type"] = "boolean" },
                // 6. integer
                ["PageCount"] = new JsonObject { ["type"] = "integer" },
                // 7. number
                ["ConfidenceScore"] = new JsonObject { ["type"] = "number" },
                // 8. contentIdentifier (document-identifier format)
                ["SourceDocument"] = new JsonObject { ["type"] = "string", ["format"] = "content-identifier" }
            }, required: new[] { "PatientName", "SourceDocument" }));

        // Response schemas for all-types-demo
        RegisterSchema("all-types.success.v1-payload", "1.0.0", "response", "all-types.success.v1",
            MakeSchemaAdvanced(new Dictionary<string, JsonObject>
            {
                ["Summary"] = new JsonObject { ["type"] = "string" },
                ["MatchedCodes"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" }
                },
                ["ReviewStatus"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { "Approved", "PendingReview", "Rejected" }
                },
                ["ProcessedAt"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
                ["RequiresFollowUp"] = new JsonObject { ["type"] = "boolean" },
                ["MatchCount"] = new JsonObject { ["type"] = "integer" },
                ["Score"] = new JsonObject { ["type"] = "number" }
            }));

        RegisterSchema("all-types.error.v1-payload", "1.0.0", "response", "all-types.error.v1",
            MakeSchema(new Dictionary<string, (string type, string? format)>
            {
                ["errorCode"] = ("string", null),
                ["errorMessage"] = ("string", null)
            }, required: new[] { "errorCode", "errorMessage" }));

        // ── Seed event aliases ───────────────────────────────────────

        _eventAliases = new List<EventAliasEntry>
        {
            // Event 1: trigger-imr-automate (both patterns, recipient required)
            new EventAliasEntry
            {
                EventAlias = "trigger-imr-automate.v1",
                BaseEventType = "IMR_Processing_Base",
                PublicationEventAliasSchema = SchemaRef("trigger-imr-automate.v1-payload", "publication"),
                InteractionPatterns = new List<InteractionPattern>
                {
                    new InteractionPattern
                    {
                        Type = "fireAndForget",
                        RecipientRequired = true,
                        AllowedRecipients = new List<string>
                        {
                            "my-automate-imr-process-named-0",
                            "my-automate-imr-process-named-1"
                        }
                    },
                    new InteractionPattern
                    {
                        Type = "requestReply",
                        RecipientRequired = true,
                        AllowedRecipients = new List<string>
                        {
                            "my-automate-imr-process-named-0",
                            "my-automate-imr-process-named-1"
                        },
                        EventInteractionModel = new EventInteractionModel
                        {
                            EimId = "eim-imr-001",
                            InitiatingEventAlias = "trigger-imr-automate.v1",
                            RequestTimeoutSeconds = 900,
                            TerminalResponses = new List<ResponseTypeEntry>
                            {
                                new ResponseTypeEntry
                                {
                                    EventAlias = "imr.completed.v1",
                                    ResponseEventAliasSchema = SchemaRef("imr.completed.v1-payload", "response")
                                },
                                new ResponseTypeEntry
                                {
                                    EventAlias = "imr.failed.v1",
                                    ResponseEventAliasSchema = SchemaRef("imr.failed.v1-payload", "response")
                                }
                            },
                            NonTerminalResponses = new List<ResponseTypeEntry>
                            {
                                new ResponseTypeEntry
                                {
                                    EventAlias = "imr.progress.v1",
                                    ResponseEventAliasSchema = SchemaRef("imr.progress.v1-payload", "response")
                                }
                            },
                            MultipleResponsesAllowed = true
                        }
                    }
                }
            },

            // Event 2: trigger-some-automate (fire-and-forget only, no recipient)
            new EventAliasEntry
            {
                EventAlias = "trigger-some-automate.v1",
                BaseEventType = "Generic_Automation_Base",
                PublicationEventAliasSchema = SchemaRef("trigger-some-automate.v1-payload", "publication"),
                InteractionPatterns = new List<InteractionPattern>
                {
                    new InteractionPattern
                    {
                        Type = "fireAndForget",
                        RecipientRequired = false
                    }
                }
            },

            // Event 3: invoice-data-extraction (requestReply, recipient optional)
            new EventAliasEntry
            {
                EventAlias = "invoice-data-extraction.v1",
                BaseEventType = "Document_Operation_Base",
                PublicationEventAliasSchema = SchemaRef("invoice-data-extraction.v1-payload", "publication"),
                InteractionPatterns = new List<InteractionPattern>
                {
                    new InteractionPattern
                    {
                        Type = "requestReply",
                        RecipientRequired = false,
                        AllowedRecipients = new List<string>
                        {
                            "invoice-processor-east",
                            "invoice-processor-west"
                        },
                        EventInteractionModel = new EventInteractionModel
                        {
                            EimId = "eim-invoice-001",
                            InitiatingEventAlias = "invoice-data-extraction.v1",
                            RequestTimeoutSeconds = 600,
                            TerminalResponses = new List<ResponseTypeEntry>
                            {
                                new ResponseTypeEntry
                                {
                                    EventAlias = "invoice.extracted.v1",
                                    ResponseEventAliasSchema = SchemaRef("invoice.extracted.v1-payload", "response")
                                },
                                new ResponseTypeEntry
                                {
                                    EventAlias = "invoice.rejected.v1",
                                    ResponseEventAliasSchema = SchemaRef("invoice.rejected.v1-payload", "response")
                                }
                            },
                            MultipleResponsesAllowed = false
                        }
                    }
                }
            },

            // Event 4: all-types-demo (requestReply, exercises all 8 field types)
            new EventAliasEntry
            {
                EventAlias = "all-types-demo.v1",
                BaseEventType = "AllTypes_Test_Base",
                PublicationEventAliasSchema = SchemaRef("all-types-demo.v1-payload", "publication"),
                InteractionPatterns = new List<InteractionPattern>
                {
                    new InteractionPattern
                    {
                        Type = "requestReply",
                        RecipientRequired = false,
                        EventInteractionModel = new EventInteractionModel
                        {
                            EimId = "eim-all-types-001",
                            InitiatingEventAlias = "all-types-demo.v1",
                            RequestTimeoutSeconds = 300,
                            TerminalResponses = new List<ResponseTypeEntry>
                            {
                                new ResponseTypeEntry
                                {
                                    EventAlias = "all-types.success.v1",
                                    ResponseEventAliasSchema = SchemaRef("all-types.success.v1-payload", "response")
                                },
                                new ResponseTypeEntry
                                {
                                    EventAlias = "all-types.error.v1",
                                    ResponseEventAliasSchema = SchemaRef("all-types.error.v1-payload", "response")
                                }
                            },
                            MultipleResponsesAllowed = false
                        }
                    }
                }
            }
        };
    }

    // ── Discovery ────────────────────────────────────────────────────

    public DiscoveryResponse Discover() =>
        new()
        {
            DiscoveredAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            PageSize = 50,
            Items = _eventAliases
        };

    // ── Schema Lookup ────────────────────────────────────────────────

    public EventAliasSchemaLookupResponse? LookupSchema(string schemaId) =>
        _schemas.TryGetValue(schemaId, out var schema) ? schema : null;

    // ── Publish ──────────────────────────────────────────────────────

    public EventAliasEntry? FindAlias(string eventAlias) =>
        _eventAliases.FirstOrDefault(a =>
            a.EventAlias.Equals(eventAlias, StringComparison.OrdinalIgnoreCase));

    public PublishResponse Publish(PublishRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        var publicationId = "pub-" + Guid.NewGuid().ToString("N")[..8];

        var response = new PublishResponse
        {
            PublicationId = publicationId,
            AcceptedAt = now.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            EventAlias = request.EventAlias,
            InteractionType = request.InteractionType
        };

        if (request.InteractionType.Equals("requestReply", StringComparison.OrdinalIgnoreCase))
        {
            var requestId = request.RequestId ?? ("req-" + Guid.NewGuid().ToString("N")[..12]);
            response.RequestId = requestId;
            response.RequestState = "pendingResponse";
            response.ResponsePollPath = $"/api/cfs-events/v1/requests/{requestId}/responses";

            // Find the EIM to determine terminal response aliases
            var alias = FindAlias(request.EventAlias);
            var rrPattern = alias?.InteractionPatterns.FirstOrDefault(p =>
                p.Type.Equals("requestReply", StringComparison.OrdinalIgnoreCase));

            _requests[requestId] = new RequestLifecycle
            {
                RequestId = requestId,
                EventAlias = request.EventAlias,
                EimId = request.EventInteractionModel?.EimId ?? string.Empty,
                AcceptedAt = now,
                ExpiresAt = now.AddSeconds(rrPattern?.EventInteractionModel?.RequestTimeoutSeconds ?? 600),
                Payload = request.Payload,
                PollCount = 0,
                TerminalResponseAlias = rrPattern?.EventInteractionModel?.TerminalResponses.FirstOrDefault()?.EventAlias ?? "unknown.completed",
                NonTerminalResponseAlias = rrPattern?.EventInteractionModel?.NonTerminalResponses?.FirstOrDefault()?.EventAlias
            };
        }

        return response;
    }

    // ── Polling ──────────────────────────────────────────────────────

    public PollingResponse? Poll(string requestId, int afterSequence)
    {
        if (!_requests.TryGetValue(requestId, out var lifecycle))
            return null;

        lifecycle.PollCount++;
        var items = new List<PollingResponseItem>();

        if (lifecycle.PollCount < PollsBeforeComplete)
        {
            // Emit a non-terminal progress response if one exists
            if (lifecycle.NonTerminalResponseAlias != null && lifecycle.PollCount == 1)
            {
                lifecycle.LastSequence++;
                items.Add(new PollingResponseItem
                {
                    Sequence = lifecycle.LastSequence,
                    EventAlias = lifecycle.NonTerminalResponseAlias,
                    ReceivedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    Terminal = false,
                    Payload = new Dictionary<string, object>
                    {
                        ["percentComplete"] = 45,
                        ["stage"] = "processing"
                    }
                });
            }

            return new PollingResponse
            {
                RequestId = requestId,
                EventAlias = lifecycle.EventAlias,
                EimId = lifecycle.EimId,
                State = "open",
                AcceptedAt = lifecycle.AcceptedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ExpiresAt = lifecycle.ExpiresAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                LastSequence = lifecycle.LastSequence,
                HasMore = items.Count > 0,
                NextAfterSequence = items.Count > 0 ? lifecycle.LastSequence : null,
                Items = items
            };
        }

        // Completed — emit terminal response
        lifecycle.LastSequence++;
        items.Add(new PollingResponseItem
        {
            Sequence = lifecycle.LastSequence,
            EventAlias = lifecycle.TerminalResponseAlias,
            ReceivedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Terminal = true,
            Payload = new Dictionary<string, object>
            {
                ["InvoiceTotal"] = "$12,450.00",
                ["DueDate"] = "2026-04-15",
                ["VendorName"] = "Contoso Ltd.",
                ["extractedTotal"] = "$12,450.00",
                ["vendorName"] = "Contoso Ltd.",
                ["documentIds"] = "8349015,8349016"
            }
        });

        return new PollingResponse
        {
            RequestId = requestId,
            EventAlias = lifecycle.EventAlias,
            EimId = lifecycle.EimId,
            State = "completed",
            AcceptedAt = lifecycle.AcceptedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ExpiresAt = lifecycle.ExpiresAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            LastSequence = lifecycle.LastSequence,
            HasMore = false,
            Items = items
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private void RegisterSchema(string schemaId, string version, string role, string eventAlias, JsonObject jsonSchema)
    {
        _schemas[schemaId] = new EventAliasSchemaLookupResponse
        {
            EventAliasSchemaId = schemaId,
            EventAliasSchemaVersion = version,
            EventAliasSchemaRole = role,
            EventAlias = eventAlias,
            ContentType = "application/schema+json",
            EventAliasSchema = jsonSchema
        };
    }

    private EventAliasSchemaReference SchemaRef(string schemaId, string role) =>
        new()
        {
            EventAliasSchemaId = schemaId,
            EventAliasSchemaVersion = "1.0.0",
            EventAliasSchemaRole = role,
            EventAliasSchemaPath = $"/api/cfs-events/v1/event-alias-schemas/{schemaId}"
        };

    private static JsonObject MakeSchema(
        Dictionary<string, (string type, string? format)> fields,
        string[]? required = null)
    {
        var props = new JsonObject();
        foreach (var (name, (type, format)) in fields)
        {
            var fieldDef = new JsonObject { ["type"] = type };
            if (format != null)
                fieldDef["format"] = format;
            props[name] = fieldDef;
        }

        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["type"] = "object",
            ["properties"] = props,
            ["additionalProperties"] = false
        };

        if (required is { Length: > 0 })
        {
            var reqArray = new JsonArray();
            foreach (var r in required)
                reqArray.Add(r);
            schema["required"] = reqArray;
        }

        return schema;
    }

    /// <summary>
    /// Creates a JSON Schema with full control over each field definition.
    /// Use this for complex types like arrays (stringList), enums (enumString), etc.
    /// </summary>
    private static JsonObject MakeSchemaAdvanced(
        Dictionary<string, JsonObject> fieldDefs,
        string[]? required = null)
    {
        var props = new JsonObject();
        foreach (var (name, def) in fieldDefs)
            props[name] = def;

        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["type"] = "object",
            ["properties"] = props,
            ["additionalProperties"] = false
        };

        if (required is { Length: > 0 })
        {
            var reqArray = new JsonArray();
            foreach (var r in required)
                reqArray.Add(r);
            schema["required"] = reqArray;
        }

        return schema;
    }

    private sealed class RequestLifecycle
    {
        public string RequestId { get; set; } = string.Empty;
        public string EventAlias { get; set; } = string.Empty;
        public string EimId { get; set; } = string.Empty;
        public DateTimeOffset AcceptedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public Dictionary<string, object> Payload { get; set; } = new();
        public int PollCount { get; set; }
        public int LastSequence { get; set; }
        public string TerminalResponseAlias { get; set; } = string.Empty;
        public string? NonTerminalResponseAlias { get; set; }
    }
}
