using Dapper;
using LN_history.Data.Internal;
using System.Net;
using LN_History.Model;
using Npgsql;
using NpgsqlTypes;

namespace LN_history.Data.DataStores;

public class ApiKeyStore : IApiKeyStore
{
    private readonly NpgsqlDataSource _dataSource;

    public ApiKeyStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
        DapperConfiguration.EnsureConfigured();
    }

    public async Task<IReadOnlyList<ApiKeyRecord>> LoadKeysAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT id, key_hash, display_prefix, role, owner_label,
                   daily_budget, burst_per_sec, max_stream_conns, expires_at, enabled
            FROM api.api_keys
            WHERE enabled = true
            """;
        var rows = await connection.QueryAsync<ApiKeyRow>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.Select(r => r.ToRecord()).ToList();
    }

    public async Task<IReadOnlyDictionary<string, int>> LoadWeightsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<(string route_key, int weight)>(
            new CommandDefinition("SELECT route_key, weight FROM api.endpoint_weights", cancellationToken: cancellationToken));
        return rows.ToDictionary(r => r.route_key, r => r.weight);
    }

    public async Task<IReadOnlyDictionary<long, long>> LoadTodaySpendAsync(DateOnly utcDay, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<(long key_id, long spent)>(new CommandDefinition(
            "SELECT key_id, sum(sum_cost) AS spent FROM api.usage WHERE usage_day = @utcDay GROUP BY key_id",
            new { utcDay }, cancellationToken: cancellationToken));
        return rows.ToDictionary(r => r.key_id, r => r.spent);
    }

    public async Task FlushUsageAsync(IReadOnlyCollection<UsageDelta> deltas, CancellationToken cancellationToken)
    {
        if (deltas.Count == 0) return;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string upsert = """
            INSERT INTO api.usage (key_id, usage_day, endpoint, sum_cost, req_count, sum_duration_ms, sum_bytes)
            VALUES (@KeyId, @UsageDay, @Endpoint, @Cost, @Count, @DurationMs, @Bytes)
            ON CONFLICT (key_id, usage_day, endpoint) DO UPDATE SET
                sum_cost        = api.usage.sum_cost        + EXCLUDED.sum_cost,
                req_count       = api.usage.req_count       + EXCLUDED.req_count,
                sum_duration_ms = api.usage.sum_duration_ms + EXCLUDED.sum_duration_ms,
                sum_bytes       = api.usage.sum_bytes       + EXCLUDED.sum_bytes
            """;
        // Dapper executes the upsert once per delta on the same connection/transaction.
        await connection.ExecuteAsync(new CommandDefinition(upsert, deltas, transaction, cancellationToken: cancellationToken));

        var activeIds = deltas.Where(d => d.KeyId > 0).Select(d => d.KeyId).Distinct().ToArray();
        if (activeIds.Length > 0)
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE api.api_keys SET last_used_at = now() WHERE id = ANY(@activeIds)",
                new { activeIds }, transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task InsertRequestLogAsync(IReadOnlyCollection<RequestLogEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0) return;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        // Binary COPY: one round trip for the whole batch, however large the backlog is.
        await using var writer = await connection.BeginBinaryImportAsync(
            "COPY api.request_log (ts, key_id, method, path, query, route_key, status, cost, charge_outcome, " +
            "duration_ms, bytes, client_ip, user_agent, trace_id) FROM STDIN (FORMAT BINARY)",
            cancellationToken);

        foreach (var e in entries)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(e.Timestamp, NpgsqlDbType.TimestampTz, cancellationToken);
            await WriteNullableStructAsync(writer, e.KeyId, NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(e.Method, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(e.Path, NpgsqlDbType.Text, cancellationToken);
            await WriteNullableAsync(writer, e.Query, NpgsqlDbType.Text, cancellationToken);
            await WriteNullableAsync(writer, e.RouteKey, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync((short)e.StatusCode, NpgsqlDbType.Smallint, cancellationToken);
            await writer.WriteAsync(e.Cost, NpgsqlDbType.Integer, cancellationToken);
            await WriteNullableAsync(writer, e.ChargeOutcome, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(e.DurationMs, NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(e.Bytes, NpgsqlDbType.Bigint, cancellationToken);
            await WriteNullableAsync(writer,
                IPAddress.TryParse(e.ClientIp, out var ip) ? ip : null, NpgsqlDbType.Inet, cancellationToken);
            await WriteNullableAsync(writer, e.UserAgent, NpgsqlDbType.Text, cancellationToken);
            await WriteNullableAsync(writer, e.TraceId, NpgsqlDbType.Text, cancellationToken);
        }

        await writer.CompleteAsync(cancellationToken);
    }

    private static Task WriteNullableAsync<T>(NpgsqlBinaryImporter writer, T? value, NpgsqlDbType type, CancellationToken ct)
        where T : class =>
        value is null ? writer.WriteNullAsync(ct) : writer.WriteAsync(value, type, ct);

    private static Task WriteNullableStructAsync<T>(NpgsqlBinaryImporter writer, T? value, NpgsqlDbType type, CancellationToken ct)
        where T : struct =>
        value is { } v ? writer.WriteAsync(v, type, ct) : writer.WriteNullAsync(ct);

    private sealed class ApiKeyRow
    {
        public long Id { get; set; }
        public string KeyHash { get; set; } = string.Empty;
        public string DisplayPrefix { get; set; } = string.Empty;
        public string Role { get; set; } = "researcher";
        public string OwnerLabel { get; set; } = string.Empty;
        public int? DailyBudget { get; set; }
        public int? BurstPerSec { get; set; }
        public int? MaxStreamConns { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool Enabled { get; set; }

        public ApiKeyRecord ToRecord() => new()
        {
            Id = Id,
            KeyHash = KeyHash.Trim(),
            DisplayPrefix = DisplayPrefix,
            Role = string.Equals(Role, "admin", StringComparison.OrdinalIgnoreCase) ? KeyRole.Admin : KeyRole.Researcher,
            OwnerLabel = OwnerLabel,
            DailyBudget = DailyBudget,
            BurstPerSec = BurstPerSec,
            MaxStreamConns = MaxStreamConns,
            ExpiresAt = ExpiresAt,
            Enabled = Enabled,
        };
    }
}
