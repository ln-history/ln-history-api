# TODO (gossip-processor): write `node_addresses.internal_id`

**Audience:** a future agent working in the **`gossip-processor`** repo (the Python
ingest service, `gossip-processor/main.py`), NOT `ln-history-api`.

**Priority:** medium — the API works without it, but address data for newly-ingested
node announcements is incomplete until this lands.

## Background

On 2026-07-21 the `ln-history-database` table `node_addresses` gained an
**`internal_id bigint`** column plus `idx_node_addresses_internal_id`. The
`ln-history-api` now resolves a node announcement's addresses by joining
`node_addresses.internal_id = node_announcements_complete.internal_id` — an integer
join that is ~950× cheaper than the old `varchar(64)` `gossip_id` join (which had no
index and cost ~1.96M per lookup).

All **existing** rows were backfilled (0 NULLs at migration time). The problem is
**new** rows: the gossip-processor still inserts `node_addresses` rows with
`internal_id = NULL`, so addresses for any node_announcement ingested after
2026-07-21 will not be found by the API's `internal_id` join.

This is the same class of bug as the 0.10.0 `internal_id` omissions
(channels / channel_updates) — the writer has the `internal_id` in hand but doesn't
persist it on the addresses.

## Current (buggy) behaviour

The node_announcement handler inserts the announcement (knowing its
`internal_id`) and then inserts each network address into `node_addresses` with only
`(gossip_id, type_id, address, port)`.

## Required change

When inserting addresses, also write the announcement's `internal_id` (the same
`internal_id` used for the `node_announcements` / `node_announcements_complete` row
the addresses belong to):

```sql
INSERT INTO node_addresses (gossip_id, type_id, address, port, internal_id)
VALUES (%s, %s, %s, %s, %s);
```

The handler already resolves `internal_id` for the announcement (see how
`_handle_channel_update` / `_handle_channel_announcement` obtain and persist it after
the 0.10.0 fix, incl. the duplicate path that must re-`SELECT internal_id` when
`ON CONFLICT (gossip_id) DO NOTHING RETURNING internal_id` yields no row). Thread that
same `internal_id` into the address inserts.

## One-time backfill of the gap

Rows inserted between 2026-07-21 and the deploy of this fix will have
`internal_id IS NULL`. Backfill them from the authoritative registry:

```sql
UPDATE node_addresses na
SET    internal_id = gi.internal_id
FROM   gossip_inventory gi
WHERE  na.internal_id IS NULL
  AND  gi.gossip_id = na.gossip_id;
```

(`gossip_inventory` is the full gossip_id → internal_id registry and had 100%
coverage during the migration. `node_announcements_complete` also works but only
covers announcement gossip_ids.)

## Verification

```sql
-- expect 0 (once fix is deployed and backfill run)
SELECT count(*) FROM node_addresses WHERE internal_id IS NULL;
```

Also confirm a freshly-ingested announcement's addresses resolve by `internal_id`:

```sql
SELECT na.* FROM node_addresses na
JOIN node_announcements_complete c ON c.internal_id = na.internal_id
WHERE c.node_id = '<some node_id>' AND c.valid_to IS NULL;
```

## Related cleanup (DBA, not the writer)

- Drop `node_addresses_old` (migration rollback copy) once confident.
- Rename the migration indexes to canonical names after the drop:
  `idx_node_addresses_new_address` → `idx_addr_lookup`,
  `node_addresses_new_pkey` → `node_addresses_pkey`,
  `idx_node_addresses_new_port` → `node_addresses_port`,
  `idx_node_addresses_new_type_id` → `node_addresses_type_id`.
