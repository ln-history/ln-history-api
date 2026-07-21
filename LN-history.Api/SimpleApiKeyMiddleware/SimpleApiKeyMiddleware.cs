using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace LN_history.Api.SimpleApiKeyMiddleware;

public class SimpleApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _apiKey;

    public SimpleApiKeyMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _apiKey = config["ApiKey"] ?? string.Empty;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Swagger is always reachable.
        if (context.Request.Path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("x-api-key", out var extractedApiKey))
        {
            await WriteProblemAsync(context, "API key is missing.");
            return;
        }

        if (extractedApiKey != _apiKey)
        {
            await WriteProblemAsync(context, "Invalid API key.");
            return;
        }

        await _next(context);
    }

    private static async Task WriteProblemAsync(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json";
        var problem = new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
            title = "Unauthorized",
            status = StatusCodes.Status401Unauthorized,
            detail
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
