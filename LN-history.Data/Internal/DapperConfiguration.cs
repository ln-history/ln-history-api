using Dapper;

namespace LN_history.Data.Internal;

internal static class DapperConfiguration
{
    private static bool _configured;

    /// <summary>
    /// Maps snake_case result columns to PascalCase row-type members so hand-written SQL
    /// materializes into the internal row records without per-column aliases.
    /// </summary>
    public static void EnsureConfigured()
    {
        if (_configured) return;
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        _configured = true;
    }
}
