-- ============================================================================
-- Migration: api.request_log — one row per authenticated API request
-- Date:      2026-10-28
-- Role:      admin (owner of the api schema; the API connects as admin)
-- Isolation: additive — a new table in the api schema, touches NO research tables.
--            Reversible with `DROP TABLE api.request_log;`.
-- ============================================================================
--
-- api.usage answers "how much did a key spend per day per endpoint". This table
-- answers "what exactly did it ask for": path, query, status, charged cost, time,
-- bytes, client IP and user agent of every request made with a key (including
-- requests refused with 429). Unauthenticated 401s are not logged — they carry
-- no key to attribute them to, and are ~99% internet scanners.
--
-- Written by the API's async flush (every ApiAccess:FlushSeconds), never on the
-- request path. The api_key query parameter is stripped before it is stored.
-- No retention job: researcher volume is tiny. Prune with
--   DELETE FROM api.request_log WHERE ts < now() - interval '1 year';
-- ============================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS api.request_log (
    ts             timestamptz NOT NULL,                   -- request start (UTC)
    key_id         bigint REFERENCES api.api_keys(id) ON DELETE CASCADE,  -- NULL => emergency admin
    method         text        NOT NULL,
    path           text        NOT NULL,
    query          text,                                   -- without api_key
    route_key      text,                                   -- cost route key; NULL => not charged
    status         smallint    NOT NULL,                   -- 101 for a WebSocket stream
    cost           integer     NOT NULL DEFAULT 0,         -- units charged (0 if refunded/refused)
    charge_outcome text,                                   -- allowed|refunded|burst_exceeded|budget_exceeded|concurrency_exceeded
    duration_ms    bigint      NOT NULL,                   -- whole connection for a stream
    bytes          bigint      NOT NULL DEFAULT 0,         -- response body bytes
    client_ip      inet,                                   -- CF-Connecting-IP, else the peer address
    user_agent     text,
    trace_id       text                                    -- jump to the trace in Tempo
);

-- No identity column, same as api.usage (no sequence to manage).
CREATE INDEX IF NOT EXISTS idx_api_request_log_key_ts ON api.request_log (key_id, ts DESC);
CREATE INDEX IF NOT EXISTS idx_api_request_log_ts     ON api.request_log (ts);

COMMIT;
