using LN_history.Api.Infrastructure;
using LN_History.Model;
using Microsoft.AspNetCore.Http;

namespace LN_history.Api.Tests;

public class RequestLogTests
{
    private static ApiKeyRecord Researcher => new() { Id = 5, Role = KeyRole.Researcher, OwnerLabel = "test" };

    private static DefaultHttpContext Request(string path, string query)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        return context;
    }

    [Test]
    public void StripApiKey_RemovesTheKey_KeepsTheRest()
    {
        var context = Request("/ln-history/v1/stream", "?types=256,257&api_key=lnh_secret&since=42");

        var query = ApiKeyAuthMiddleware.StripApiKey(context.Request.Query);

        Assert.Multiple(() =>
        {
            Assert.That(query, Does.Not.Contain("lnh_secret"));
            Assert.That(query, Does.Contain("since=42"));
            Assert.That(query, Does.Contain("types=256%2C257"));
        });
    }

    [Test]
    public void StripApiKey_ReturnsNull_WhenOnlyTheKeyWasPresent()
    {
        var context = Request("/ln-history/v1/stream", "?API_KEY=lnh_secret");
        Assert.That(ApiKeyAuthMiddleware.StripApiKey(context.Request.Query), Is.Null);
    }

    [Test]
    public void BuildEntry_RecordsChargeClientAndStatus()
    {
        var context = Request("/ln-history/v1/snapshot/2026-01-01T00:00:00Z", "?withUpdates=true");
        context.Request.Headers["CF-Connecting-IP"] = "203.0.113.7";
        context.Request.Headers.UserAgent = "ureq/2.10.1";
        context.Response.StatusCode = 429;
        ApiKeyContext.SetCharge(context, "snapshot_with_updates", 0, "concurrency_exceeded");

        var entry = ApiKeyAuthMiddleware.BuildEntry(context, Researcher, DateTime.UtcNow, 12, 300, threw: false);

        Assert.Multiple(() =>
        {
            Assert.That(entry.KeyId, Is.EqualTo(5));
            Assert.That(entry.Path, Is.EqualTo("/ln-history/v1/snapshot/2026-01-01T00:00:00Z"));
            Assert.That(entry.Query, Is.EqualTo("withUpdates=true"));
            Assert.That(entry.StatusCode, Is.EqualTo(429));
            Assert.That(entry.RouteKey, Is.EqualTo("snapshot_with_updates"));
            Assert.That(entry.ChargeOutcome, Is.EqualTo("concurrency_exceeded"));
            Assert.That(entry.Cost, Is.EqualTo(0));
            Assert.That(entry.ClientIp, Is.EqualTo("203.0.113.7"));
            Assert.That(entry.UserAgent, Is.EqualTo("ureq/2.10.1"));
        });
    }

    [Test]
    public void BuildEntry_EmergencyAdmin_HasNoKeyId_AndExceptionIs500()
    {
        var context = Request("/ln-history/v1/stats/network", "");
        var admin = new ApiKeyRecord { Id = 0, Role = KeyRole.Admin };

        var entry = ApiKeyAuthMiddleware.BuildEntry(context, admin, DateTime.UtcNow, 1, 0, threw: true);

        Assert.Multiple(() =>
        {
            Assert.That(entry.KeyId, Is.Null);
            Assert.That(entry.StatusCode, Is.EqualTo(500));
            Assert.That(entry.RouteKey, Is.Null);
        });
    }
}
