using System.Net.Http.Headers;
using System.Text;
using Bitcoin.Data.DataStores;
using Bitcoin.Data.Rpc;

namespace Bitcoin.Data.Tests;

// Integration tests that hit a live Bitcoin Core node. Configure via environment:
//   BITCOIN_RPC_HOST, BITCOIN_RPC_PORT, BITCOIN_RPC_USER, BITCOIN_RPC_PASSWORD
// Skipped automatically when those are not set (e.g. in CI).
[TestFixture]
[Category("Integration")]
public class BlockDataStoreIntegrationTests
{
    // Known values for mainnet block 800000 (verified against the node 2026-07-21).
    private const long Height = 800000;
    private const string Hash = "00000000000000000002a7c4c1e48d76c5a37902165a270156b7a8d72728a054";
    private const long SubsidySat = 625000000;
    private const long TxFees = 13687680;
    private const long SizeBytes = 1634261;
    private const long TimeUnix = 1690168629;
    private const int TxCount = 3721;

    private BlockDataStore _store = null!;
    private HttpClient _http = null!;

    [SetUp]
    public void SetUp()
    {
        var host = Environment.GetEnvironmentVariable("BITCOIN_RPC_HOST");
        var port = Environment.GetEnvironmentVariable("BITCOIN_RPC_PORT") ?? "8332";
        var user = Environment.GetEnvironmentVariable("BITCOIN_RPC_USER");
        var password = Environment.GetEnvironmentVariable("BITCOIN_RPC_PASSWORD");

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
        {
            Assert.Ignore("Bitcoin RPC environment variables not set; skipping integration test.");
        }

        _http = new HttpClient { BaseAddress = new Uri($"http://{host}:{port}/") };
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        _store = new BlockDataStore(new BitcoinRpcClient(_http));
    }

    [TearDown]
    public void TearDown() => _http?.Dispose();

    [Test]
    public async Task GetByHeight_MapsAllFields()
    {
        var block = await _store.GetByHeightAsync(Height, CancellationToken.None);

        Assert.That(block, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(block!.BlockHash, Is.EqualTo(Hash));
            Assert.That(block.BlockHeight, Is.EqualTo(Height));
            Assert.That(block.SubsidySat, Is.EqualTo(SubsidySat));
            Assert.That(block.TxFees, Is.EqualTo(TxFees));
            Assert.That(block.SpaceBytes, Is.EqualTo(SizeBytes));
            Assert.That(block.TxCount, Is.EqualTo(TxCount));
            Assert.That(block.Timestamp, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(TimeUnix).UtcDateTime));
        });
    }

    [Test]
    public async Task GetByHash_ReturnsSameBlock()
    {
        var block = await _store.GetByHashAsync(Hash, CancellationToken.None);
        Assert.That(block, Is.Not.Null);
        Assert.That(block!.BlockHeight, Is.EqualTo(Height));
    }

    [Test]
    public async Task GetByTimestamp_ReturnsLastBlockAtOrBeforeTarget()
    {
        var target = DateTimeOffset.FromUnixTimeSeconds(TimeUnix).UtcDateTime;
        var block = await _store.GetByTimestampAsync(target, CancellationToken.None);

        Assert.That(block, Is.Not.Null);
        // The block at exactly this time is 800000; result must be at-or-before the target.
        Assert.That(block!.Timestamp, Is.LessThanOrEqualTo(target));
        Assert.That(block.BlockHeight, Is.InRange(Height - 1, Height + 1));
    }

    [Test]
    public async Task GetByHeight_OutOfRange_ReturnsNull()
    {
        var block = await _store.GetByHeightAsync(long.MaxValue, CancellationToken.None);
        Assert.That(block, Is.Null);
    }
}
