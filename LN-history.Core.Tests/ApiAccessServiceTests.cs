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
