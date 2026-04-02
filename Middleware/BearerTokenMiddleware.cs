using System.Net;

namespace DummyCicServer.Middleware;

/// <summary>
/// Dummy bearer-token validation middleware.
/// Accepts any request whose Authorization header is "Bearer {non-empty-value}".
/// Returns 401 Unauthorized when the header is missing or malformed.
/// </summary>
public sealed class BearerTokenMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<BearerTokenMiddleware> _logger;

    public BearerTokenMiddleware(RequestDelegate next, ILogger<BearerTokenMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip auth for the health endpoint
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(authHeader))
        {
            _logger.LogWarning("Missing Authorization header from {RemoteIp}", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing Authorization header" });
            return;
        }

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Malformed Authorization header (expected 'Bearer <token>')");
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Authorization header must use Bearer scheme" });
            return;
        }

        var token = authHeader["Bearer ".Length..].Trim();

        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("Empty bearer token");
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Bearer token must not be empty" });
            return;
        }

        // Dummy validation: any non-empty token is accepted.
        _logger.LogInformation("Bearer token accepted (dummy validation)");
        await _next(context);
    }
}
