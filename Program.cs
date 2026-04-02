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
// 1. EVENT DISCOVERY
//    GET /event-discovery?eventType=cfs.custom.event.v1
// ═════════════════════════════════════════════════════════════════════
app.MapGet("/event-discovery", (
    [FromQuery] string eventType,
    EventStore store,
    ILogger<Program> logger) =>
{
    logger.LogInformation("Event discovery requested for type '{EventType}'", eventType);
    var response = store.Discover(eventType);
    return Results.Ok(response);
})
.WithName("EventDiscovery");

// ═════════════════════════════════════════════════════════════════════
// 2. FIRE EVENT  (Request/Reply  OR  Fire-and-Forget)
//    POST /api/system-integrations/events
// ═════════════════════════════════════════════════════════════════════
app.MapPost("/api/system-integrations/events", (
    HttpContext http,
    [FromBody] FireEventRequest request,
    EventStore store,
    ILogger<Program> logger) =>
{
    var correlationId = http.Request.Headers["X-correlation-id"].FirstOrDefault();
    var ttlHeader = http.Request.Headers["X-Event-TTL"].FirstOrDefault();
    int? ttlSeconds = int.TryParse(ttlHeader, out var ttl) ? ttl : null;

    var alias = store.FindAlias(request.EventType);
    if (alias is null)
    {
        logger.LogWarning("Unknown event alias '{AliasId}'", request.EventType);
        return Results.NotFound(new { error = $"Event alias '{request.EventType}' not found" });
    }

    logger.LogInformation(
        "Firing event '{AliasName}' (pattern={Pattern})",
        alias.AliasName, alias.Pattern.Pattern);

    var jobId = store.CreateJob(request.EventType, correlationId, request.Payload, ttlSeconds);

    // Fire-and-Forget → 202 Accepted, no jobId
    if (alias.Pattern.Pattern.Equals("fireAndForget", StringComparison.OrdinalIgnoreCase))
    {
        logger.LogInformation("Fire-and-forget event accepted");
        return Results.Json(
            new FireAndForgetResponse
            {
                Status = "accepted",
                IntegrationId = "integrationA",
                CorrelationId = correlationId
            },
            statusCode: 202);
    }

    // Request/Reply → 200 OK with jobId for polling
    var expiresAt = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds ?? 600);
    logger.LogInformation("Request/reply event accepted, jobId={JobId}", jobId);
    return Results.Ok(new RequestReplyResponse
    {
        Status = "accepted",
        JobId = jobId,
        IntegrationId = "integrationA",
        TtlSeconds = ttlSeconds ?? 600,
        ExpiresAt = expiresAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        CorrelationId = correlationId
    });
})
.WithName("FireEvent");

// ═════════════════════════════════════════════════════════════════════
// 3. POLL FOR EVENT COMPLETION  (Request/Reply only)
//    POST /api/system-integrations/jobs/{jobId}
// ═════════════════════════════════════════════════════════════════════
app.MapPost("/api/system-integrations/jobs/{jobId}", (
    string jobId,
    HttpContext http,
    EventStore store,
    ILogger<Program> logger) =>
{
    logger.LogInformation("Polling job '{JobId}'", jobId);
    var result = store.PollJob(jobId);
    if (result is null)
    {
        logger.LogWarning("Job '{JobId}' not found", jobId);
        return Results.NotFound(new { error = $"Job '{jobId}' not found" });
    }

    logger.LogInformation("Job '{JobId}' status: {Status} (poll #{PollCount})",
        jobId, result.Status, result.Status == "Completed" ? "final" : "pending");

    return Results.Ok(result);
})
.WithName("PollJob");

// ═════════════════════════════════════════════════════════════════════
// 4. DELETE JOB  (cleanup after request/reply completes or times out)
//    DELETE /api/system-integrations/jobs/{jobId}
// ═════════════════════════════════════════════════════════════════════
app.MapDelete("/api/system-integrations/jobs/{jobId}", (
    string jobId,
    EventStore store,
    ILogger<Program> logger) =>
{
    logger.LogInformation("Deleting job '{JobId}'", jobId);
    return store.DeleteJob(jobId)
        ? Results.NoContent()
        : Results.NotFound(new { error = $"Job '{jobId}' not found" });
})
.WithName("DeleteJob");

app.Run();
