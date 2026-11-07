using LN_history.Api.Controllers;
using LN_history.Api.Infrastructure;

namespace LN_history.Api.Tests;

public class CostEnforcementFilterTests
{
    [TestCase(400)]
    [TestCase(404)]
    public void ShouldRefund_ClientError(int status) =>
        Assert.That(CostEnforcementFilter.ShouldRefund(null, status), Is.True);

    [TestCase(200)]
    [TestCase(500)]
    [TestCase(503)]
    public void ShouldNotRefund_SuccessOrServerError(int status) =>
        Assert.That(CostEnforcementFilter.ShouldRefund(null, status), Is.False);

    [Test]
    public void ShouldNotRefund_WhenHandlerThrew()
    {
        // A statement timeout surfaces as an exception; the DB already did the work.
        var timeout = new InvalidOperationException("canceling statement due to statement timeout");
        Assert.That(CostEnforcementFilter.ShouldRefund(timeout, 500), Is.False);
    }

    [Test]
    public void DiffWindow_IsTwoHours() =>
        Assert.That(SnapshotController.MaxDiffWindow, Is.EqualTo(TimeSpan.FromHours(2)));
}
