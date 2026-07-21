using LN_History.Model;

namespace LN_history.Core.Tests;

public class ShortChannelIdTests
{
    // Real value observed in the database: scid 1054414059077500929 => 958984x3454x1
    [Test]
    public void Decodes_RealWorld_Scid()
    {
        var scid = new ShortChannelId(1054414059077500929);

        Assert.Multiple(() =>
        {
            Assert.That(scid.BlockHeight, Is.EqualTo(958984));
            Assert.That(scid.TransactionIndex, Is.EqualTo(3454));
            Assert.That(scid.OutputIndex, Is.EqualTo(1));
            Assert.That(scid.ToString(), Is.EqualTo("958984x3454x1"));
        });
    }

    [Test]
    public void FromParts_RoundTrips_ToLong()
    {
        var scid = ShortChannelId.FromParts(958984, 3454, 1);
        Assert.That(scid.Value, Is.EqualTo(1054414059077500929));
    }

    [Test]
    public void TryParse_Accepts_HumanForm()
    {
        Assert.That(ShortChannelId.TryParse("865123x1x0", out var scid), Is.True);
        Assert.That(scid.BlockHeight, Is.EqualTo(865123));
        Assert.That(scid.TransactionIndex, Is.EqualTo(1));
        Assert.That(scid.OutputIndex, Is.EqualTo(0));
    }

    [Test]
    public void TryParse_Accepts_IntegerForm()
    {
        Assert.That(ShortChannelId.TryParse("1054414059077500929", out var scid), Is.True);
        Assert.That(scid.ToString(), Is.EqualTo("958984x3454x1"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("abc")]
    [TestCase("1x2")]
    [TestCase("1x2x3x4")]
    [TestCase("-5")]
    [TestCase("1x-2x3")]
    public void TryParse_Rejects_Invalid(string input)
    {
        Assert.That(ShortChannelId.TryParse(input, out _), Is.False);
    }

    [Test]
    public void ToString_Parse_RoundTrip()
    {
        // realistic maxima: block heights stay well under 2^23, so the sign bit is never set
        foreach (var value in new long[] { 0, 1, 65535, 1054414059077500929, ((long)8_388_607 << 40) | ((long)16777215 << 16) | 65535 })
        {
            var scid = new ShortChannelId(value);
            Assert.That(ShortChannelId.Parse(scid.ToString()).Value, Is.EqualTo(value), $"round-trip failed for {value}");
        }
    }
}
