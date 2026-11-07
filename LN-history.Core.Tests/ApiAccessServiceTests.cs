using LN_history.Core.Auth;
using LN_History.Model;

namespace LN_history.Core.Tests;

public class ApiAccessServiceTests
{
    private static ApiKeyRecord Researcher(long id = 1) =>
        new() { Id = id, Role = KeyRole.Researcher, OwnerLabel = "test" };

    [Test]
    public void HeavySlot_AllowsOnlyOneAtATime_PerKey()
    {
        var access = new ApiAccessService(new ApiAccessOptions { MaxConcurrentHeavyPerKey = 1 });
        var key = Researcher();

        Assert.Multiple(() =>
        {
            Assert.That(access.TryAcquireHeavy(key), Is.True);
            Assert.That(access.TryAcquireHeavy(key), Is.False);
        });

        access.ReleaseHeavy(key);
        Assert.That(access.TryAcquireHeavy(key), Is.True);
    }

    [Test]
    public void HeavySlot_IsIndependent_AcrossKeys()
    {
        var access = new ApiAccessService(new ApiAccessOptions { MaxConcurrentHeavyPerKey = 1 });

        Assert.Multiple(() =>
        {
            Assert.That(access.TryAcquireHeavy(Researcher(1)), Is.True);
            Assert.That(access.TryAcquireHeavy(Researcher(2)), Is.True);
        });
    }

    [Test]
    public void HeavySlot_IsUnlimited_ForAdmins()
    {
        var access = new ApiAccessService(new ApiAccessOptions { MaxConcurrentHeavyPerKey = 1 });
        var admin = new ApiKeyRecord { Id = 9, Role = KeyRole.Admin };

        Assert.Multiple(() =>
        {
            Assert.That(access.TryAcquireHeavy(admin), Is.True);
            Assert.That(access.TryAcquireHeavy(admin), Is.True);
        });
    }

    [Test]
    public void ReleaseHeavy_WithoutAcquire_DoesNotGrantExtraSlots()
    {
        var access = new ApiAccessService(new ApiAccessOptions { MaxConcurrentHeavyPerKey = 1 });
        var key = Researcher();

        access.TryAcquireHeavy(key);
        access.ReleaseHeavy(key);
        access.ReleaseHeavy(key); // double release must not go negative

        Assert.Multiple(() =>
        {
            Assert.That(access.TryAcquireHeavy(key), Is.True);
            Assert.That(access.TryAcquireHeavy(key), Is.False);
        });
    }
}

public class RequestLogBufferTests
{
    private static RequestLogEntry Entry(string path) => new() { Path = path, Method = "GET" };

    [Test]
    public void Drain_ReturnsBufferedRequests_InOrder_AndEmptiesTheBuffer()
    {
        var access = new ApiAccessService(new ApiAccessOptions());
        access.RecordRequest(Entry("/a"));
        access.RecordRequest(Entry("/b"));

        var drained = access.DrainPendingRequests();

        Assert.Multiple(() =>
        {
            Assert.That(drained.Select(e => e.Path), Is.EqualTo(new[] { "/a", "/b" }));
            Assert.That(access.DrainPendingRequests(), Is.Empty);
        });
    }

    [Test]
    public void FullBuffer_DropsAndCounts_InsteadOfGrowing()
    {
        var access = new ApiAccessService(new ApiAccessOptions { MaxPendingRequestLog = 2 });
        for (var i = 0; i < 5; i++) access.RecordRequest(Entry($"/{i}"));

        Assert.Multiple(() =>
        {
            Assert.That(access.DrainPendingRequests(), Has.Count.EqualTo(2));
            Assert.That(access.TakeDroppedRequestCount(), Is.EqualTo(3));
            Assert.That(access.TakeDroppedRequestCount(), Is.EqualTo(0));
        });
    }
}
