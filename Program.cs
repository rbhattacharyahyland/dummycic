using System.Text.Json;
using System.Text.Json.Serialization;
using DummyCicServer.Middleware;
using DummyCicServer.Models;
using DummyCicServer.Services;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<EventStore>();
builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    opts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

// ── Dummy bearer-token validation ────────────────────────────────────
app.UseMiddleware<BearerTokenMiddleware>();

// ── Health check (no auth) ───────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .WithName("Health");

// ═════════════════════════════════════════════════════════════════════
// 1. DISCOVERY
//    GET /api/cfs-events/v1/discovery
// ═════════════════════════════════════════════════════════════════════
app.MapGet("/api/cfs-events/v1/discovery", (
    [FromQuery] string? pageToken,
    [FromQuery] int? pageSize,
    EventStore store,
    ILogger<Program> logger) =>
{
    logger.LogInformation("Discovery requested (pageToken={PageToken})", pageToken);
    var response = store.Discover();
    // Single-page mock — always return all items with no nextPageToken
    return Results.Ok(response);
})
.WithName("Discovery");

// ═════════════════════════════════════════════════════════════════════
// 2. SCHEMA LOOKUP
//    GET /api/cfs-events/v1/event-alias-schemas/{eventAliasSchemaId}
// ═════════════════════════════════════════════════════════════════════
app.MapGet("/api/cfs-events/v1/event-alias-schemas/{eventAliasSchemaId}", (
    string eventAliasSchemaId,
    [FromQuery] string? eventAliasSchemaVersion,
    EventStore store,
    ILogger<Program> logger) =>
{
    logger.LogInformation("Schema lookup for '{SchemaId}'", eventAliasSchemaId);
    var schema = store.LookupSchema(eventAliasSchemaId);
    if (schema is null)
    {
        logger.LogWarning("Schema '{SchemaId}' not found", eventAliasSchemaId);
        return Results.NotFound(new ErrorResponse
        {
            Error = new ErrorDetail
            {
                Code = "SCHEMA_NOT_FOUND",
                Message = $"No event alias schema was found for '{eventAliasSchemaId}'."
            }
        });
    }

    return Results.Ok(schema);
})
.WithName("SchemaLookup");

// ═════════════════════════════════════════════════════════════════════
// 3. PUBLISH
//    POST /api/cfs-events/v1/publish
// ═════════════════════════════════════════════════════════════════════
app.MapPost("/api/cfs-events/v1/publish", (
    [FromBody] PublishRequest request,
    EventStore store,
    ILogger<Program> logger) =>
{
    var alias = store.FindAlias(request.EventAlias);
    if (alias is null)
    {
        logger.LogWarning("Unknown event alias '{EventAlias}'", request.EventAlias);
        return Results.UnprocessableEntity(new ErrorResponse
        {
            Error = new ErrorDetail
            {
                Code = "UNKNOWN_EVENT_ALIAS",
                Message = $"Event alias '{request.EventAlias}' not found."
            }
        });
    }

    logger.LogInformation(
        "Publishing event '{EventAlias}' (interactionType={Type})",
        request.EventAlias, request.InteractionType);

    var response = store.Publish(request);
    return Results.Json(response, statusCode: 202);
})
.WithName("Publish");

// ═════════════════════════════════════════════════════════════════════
// 4. POLL RESPONSES
//    GET /api/cfs-events/v1/requests/{requestId}/responses
// ═════════════════════════════════════════════════════════════════════
app.MapGet("/api/cfs-events/v1/requests/{requestId}/responses", (
    string requestId,
    [FromQuery] int? afterSequence,
    [FromQuery] int? limit,
    EventStore store,
    ILogger<Program> logger) =>
{
    logger.LogInformation("Polling responses for requestId='{RequestId}' (afterSequence={After})",
        requestId, afterSequence);

    var result = store.Poll(requestId, afterSequence ?? 0);
    if (result is null)
    {
        logger.LogWarning("Request '{RequestId}' not found", requestId);
        return Results.NotFound(new ErrorResponse
        {
            Error = new ErrorDetail
            {
                Code = "REQUEST_NOT_FOUND",
                Message = $"No request lifecycle was found for '{requestId}'.",
                RequestId = requestId
            }
        });
    }

    logger.LogInformation("Request '{RequestId}' state={State}, lastSequence={Seq}",
        requestId, result.State, result.LastSequence);

    return Results.Ok(result);
})
.WithName("PollResponses");

app.Run();
