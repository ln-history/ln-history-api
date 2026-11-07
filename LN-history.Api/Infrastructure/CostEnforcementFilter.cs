using System.Diagnostics;
using LN_history.Api.Instrumentation;
using LN_history.Core.Auth;
using LN_History.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace LN_history.Api.Infrastructure;

/// <summary>
/// Global resource filter that pre-charges a request's endpoint weight against the key's burst bucket
/// and daily budget BEFORE the action runs (so an over-budget heavy query is rejected without touching
/// the DB), records measured actuals afterwards, and refunds the weight only when the request was
/// rejected as a client error (4xx). A timeout or 5xx is NOT refunded: the DB did the work, and a free
/// failure would let a key run 60s queries for nothing. Heavy actions also hold a per-key concurrency
/// slot for their whole duration. No-ops
/// when there is no principal (auth disabled) or the action declares no <see cref="ResourceCostAttribute"/>.
/// </summary>
public sealed class CostEnforcementFilter : IAsyncResourceFilter
{
    private readonly IApiAccessService _access;
    private readonly AppMetrics _metrics;
    private readonly ILogger<CostEnforcementFilter> _logger;

    public CostEnforcementFilter(IApiAccessService access, AppMetrics metrics, ILogger<CostEnforcementFilter> logger)
    {
        _access = access;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        var record = ApiKeyContext.Get(http);
        var cost = ResolveCost(http);

        // No principal (auth off) or no cost attribute (e.g. the self-managed WS stream) -> pass through.
        if (record is null || cost is null)
        {
            await next();
            return;
        }

        var (routeKey, weight, heavy) = cost.Value;

        // Take the heavy slot BEFORE charging, so a request refused for concurrency costs nothing.
        if (heavy && !_access.TryAcquireHeavy(record))
        {
            _metrics.ChargeResults.Add(1,
                new KeyValuePair<string, object?>("route_key", routeKey),
                new KeyValuePair<string, object?>("outcome", "concurrency_exceeded"));
            _logger.LogWarning("Rate-limited {Owner} on {RouteKey}: a heavy request is already running.",
                record.OwnerLabel, routeKey);
            http.Response.Headers.RetryAfter = "5";
            context.Result = TooManyHeavy();
            return;
        }

        try
        {
            await ChargeAndExecuteAsync(context, next, record, routeKey, weight);
        }
        finally
        {
            if (heavy) _access.ReleaseHeavy(record);
        }
    }

    private async Task ChargeAndExecuteAsync(
        ResourceExecutingContext context, ResourceExecutionDelegate next,
        ApiKeyRecord record, string routeKey, int weight)
    {
        var http = context.HttpContext;
        var charge = _access.Charge(record, weight);

        _metrics.ChargeResults.Add(1,
            new KeyValuePair<string, object?>("route_key", routeKey),
            new KeyValuePair<string, object?>("outcome", OutcomeTag(charge.Outcome)));

        if (!record.IsAdmin)
        {
            http.Response.Headers["X-RateLimit-Remaining"] = charge.RemainingBudget.ToString();
            http.Response.Headers["X-RateLimit-Reset"] = charge.ResetUnixSeconds.ToString();
        }

        if (!charge.Allowed)
        {
            http.Response.Headers.RetryAfter = charge.RetryAfterSeconds.ToString();
            _logger.LogWarning(
                "Rate-limited {Owner} on {RouteKey}: {Outcome}, {Remaining} budget units left, resets at {Reset}.",
                record.OwnerLabel, routeKey, charge.Outcome, charge.RemainingBudget, charge.ResetUnixSeconds);
            context.Result = TooMany(charge);
            return; // handler never runs; DB untouched
        }

        var sw = Stopwatch.StartNew();
        var executed = await next();
        sw.Stop();

        var refunded = ShouldRefund(executed.Exception, http.Response.StatusCode);
        if (refunded)
        {
            _access.Refund(record, weight);
            _metrics.ChargeRefunds.Add(weight, new KeyValuePair<string, object?>("route_key", routeKey));
        }

        var bytes = http.Response.ContentLength ?? 0;
        _access.RecordActuals(record, routeKey, refunded ? 0 : weight, sw.ElapsedMilliseconds, bytes);
    }

    /// <summary>
    /// Refund only a clean 4xx: the handler rejected the input (bad timestamp, window too wide, not
    /// found) after at most a cheap lookup. Exceptions — including statement timeouts and client
    /// aborts mid-query — and 5xx stay charged, because the DB paid for them.
    /// </summary>
    public static bool ShouldRefund(Exception? exception, int statusCode) =>
        exception is null && statusCode is >= 400 and < 500;

    private (string RouteKey, int Weight, bool Heavy)? ResolveCost(HttpContext http)
    {
        var attr = http.GetEndpoint()?.Metadata.GetMetadata<ResourceCostAttribute>();
        if (attr is null) return null;

        var routeKey = attr.Key;
        // A snapshot with channel updates is materially heavier -> charge it as a distinct key.
        if (routeKey == "snapshot"
            && http.Request.Query.TryGetValue("withUpdates", out var wu)
            && bool.TryParse(wu, out var on) && on)
            routeKey = "snapshot_with_updates";

        return (routeKey, _access.ResolveWeight(routeKey, attr.Default), attr.Heavy);
    }

    private static string OutcomeTag(ChargeOutcome outcome) => outcome switch
    {
        ChargeOutcome.Allowed => "allowed",
        ChargeOutcome.BurstExceeded => "burst_exceeded",
        ChargeOutcome.BudgetExceeded => "budget_exceeded",
        _ => "unknown",
    };

    private static ObjectResult TooManyHeavy()
    {
        var problem = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.29",
            Title = "Too Many Requests",
            Status = StatusCodes.Status429TooManyRequests,
            Detail = "Another heavy request (snapshot, snapshot-diff or stream backfill) is still running for this key. Wait for it to finish.",
        };
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status429TooManyRequests };
    }

    private static ObjectResult TooMany(ChargeResult charge)
    {
        var detail = charge.Outcome == ChargeOutcome.BurstExceeded
            ? "Burst rate exceeded. Slow down and retry."
            : "Daily cost budget exhausted. Retry after the reset.";
        var problem = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.29",
            Title = "Too Many Requests",
            Status = StatusCodes.Status429TooManyRequests,
            Detail = detail,
        };
        problem.Extensions["remaining_budget"] = charge.RemainingBudget;
        problem.Extensions["reset_unix"] = charge.ResetUnixSeconds;
        return new ObjectResult(problem) { StatusCode = StatusCodes.Status429TooManyRequests };
    }
}
