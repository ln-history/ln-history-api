# ln-history-api — Refactor Plan

Status: agreed via design interview 2026-07-21. Target: clean rebuild of the three
layers (API → Core → Data) + Bitcoin RPC access, against the **live** DB schema
(not the stale `ln-history-database` skill blueprint).

---

## 1. Architecture

Strict 3-layer, one-directional dependencies:

```
LN-history.Api      controllers, DTOs, DTO<->domain mapping, auth, errors, swagger
      │  uses
LN-history.Core     business logic / orchestration; returns domain models
      │  uses
LN-history.Data     Postgres (Dapper + NpgsqlDataSource), hand-written SQL
Bitcoin.Data        Bitcoin Core JSON-RPC client (part of the Data tier)
LN-History.Model    shared domain models + enums (referenced by all)
LN-history.Startup   composition root (Program.cs), DI wiring, config
```

- API never touches Npgsql/RPC. Core never touches HttpContext/DTOs. Data never
  references Core/Api.
- Bitcoin.Data is a Data-tier sibling consumed by Core.

## 2. Project topology (keep / rewrite / cut)

| Project | Action |
|---|---|
| `LN-history.Api` | **Rewrite internals** — new DTOs, controllers, mappers, auth, errors |
| `LN-history.Core` | **Rewrite internals** — real services (currently empty stubs); fold in `Bitcoin.Core`'s block-identification logic |
| `LN-history.Data` | **Rewrite internals** — Dapper + `NpgsqlDataSource`; delete DuckDB path |
| `Bitcoin.Data` | **Rewrite** — delete CouchDB; add lean Bitcoin RPC client |
| `LN-History.Model` | **Keep, expand** — full domain model set |
| `LN-history.Startup` | **Rewrite `Program.cs`** wiring |
| `Bitcoin.Core` | **Delete** — fold into `LN-history.Core` |
| `LN-history.Cache` | **Out of scope** — leave untouched, not referenced |
| `LightningGraph` (+ tests) | **Out of scope** — leave untouched |
| Test projects | Keep those for surviving projects; empty `UnitTest1.cs` stubs may stay dormant |

## 3. Package changes

**Remove:** `Dapper.FluentMap.Dommel`, `DuckDB.NET.Data(.Full)`, `net-questdb-client`,
`Npgsql.EntityFrameworkCore.PostgreSQL`, all `CouchDB.NET*`,
`Microsoft.EntityFrameworkCore.*` (Sqlite/Tools/Design), `Microsoft.AspNetCore.Mvc.NewtonsoftJson`,
`Swashbuckle.AspNetCore.Newtonsoft`, `AutoMapper` (mapping done by hand — see §8).

**Add:** `Npgsql` + `Npgsql.DependencyInjection` (for `AddNpgsqlDataSource`) in `LN-history.Data`.
Keep `Dapper`. Bitcoin.Data needs no new packages (built-in `HttpClient` + `System.Text.Json`).

Framework stays **net8.0** across all projects.

## 4. Data layer (`LN-history.Data`)

- Register a **singleton `NpgsqlDataSource`** from `ConnectionStrings:PostgreSQL`.
  Each data store opens a short-lived connection per query (`await using`). No shared
  connection, no controller-opened connections.
- Hand-written SQL, Dapper for materialization. **Every heavy query is `EXPLAIN`-checked
  against the 1,000,000 cost gate before shipping.**

Data stores (interfaces + impls):

- `IChannelDataStore` — `GetByScid`, `GetActiveAtTimestamp(paged)`, `GetByNode(paged)`,
  `GetUpdateHistory(scid)`, `GetActivePolicies(scid, ts)`, `GetUpdateCounts(scid)`.
- `INodeDataStore` — `GetById`, `GetActiveAtTimestamp(paged)`, `GetAnnouncementHistory`,
  `GetOpenDegree(nodeId)`, `GetAllTimeDegree(nodeId)`, `GetAddresses(gossipId)`.
- `IClosureDataStore` — `GetByScid` (join `channels`→`channel_closures`).
- `ISnapshotDataStore` — `GetSnapshotRaw(ts, withUpdates)`, `GetDiffRaw(start,end)`,
  `GetDiffEvents(start,end)`.
- `IStatsDataStore` — `TopChannels(by, limit)`, `TopNodes(by, limit)`,
  `NetworkStats(ts)`, `ClosureStats(from,to)`.

Key column facts (live schema, verified 2026-07-21):
- `channels` (~497K): `funding_timestamp`/`closing_timestamp`, `capacity_sat`,
  `source_node_id`/`target_node_id`, `raw_gossip`, `internal_id`. No `capacity_sat` index.
- `channel_updates` (~145M): temporal chain `valid_from`/`valid_to`, `direction bit(1)`,
  policy columns, `is_fee_update`/`is_topology_update`.
- `channel_update_counts` (~412K): `count_direction_0/1` (each indexed), PK `scid`,
  `refreshed_at` (aggregate, may lag).
- `nodes` (~53K): `first_seen`/`last_seen`, `announcement_count` (precomputed).
- `node_announcements` (~95K, active-head indexed) / `node_announcements_complete` (~26M, full history).
- `node_addresses` (~66M) + `address_types` (5 rows).
- `channel_closures` (~448K): `closing_txid`, `type closure_reason`, `mining_fee_sat`, …

## 5. Bitcoin.Data — RPC client

- Lean hand-rolled JSON-RPC over `HttpClient` (typed client via `IHttpClientFactory`),
  HTTP Basic auth, `System.Text.Json`. Bind to config section **`Bitcoind`**
  (`RPCHost`/`RPCPort`/`RPCUser`/`RPCPassword`). Delete CouchDB code and the stale
  `BitcoinNode` lookup. Fulcrum: **out of scope** (bitcoind has `txindex=1`).
- `IBitcoinRpcClient`: `GetBlockCount`, `GetBlockHash(height)`, `GetBlock(hash)`,
  `GetBlockStats(hashOrHeight)`, `GetRawTransaction(txid)`.
- `IBlockDataStore`: `GetByHeight`, `GetByHash`, `GetByTimestamp` (**binary search**,
  last block at-or-before T, ~20 RPC calls), `GetClosingTx(txid)`.
- `BlockDto` sourcing: hash/height/time/size from `getblock`; `subsidy_sat`=`getblockstats.subsidy`,
  `tx_fees`=`getblockstats.totalfee`, `space_bytes`=`getblock.size` (or `getblockstats.total_size`).

## 6. Core layer (`LN-history.Core`)

Services orchestrate data stores + Bitcoin.Data and apply logic (expansion toggles,
assembling `fee_policies`, closure attachment). Return **domain models**, not DTOs.

- `IChannelService` — get single (with optional node expansion + policies + raw),
  list-at-time, by-node, update history.
- `INodeService` — get single (open/all-time degree), list-at-time, announcement history.
- `ISnapshotService` — raw snapshot, raw/parsed diff.
- `IBlockService` — by height/hash/timestamp (folds old `Bitcoin.Core` logic).
- `IStatsService` — top channels/nodes, network, closures.

## 7. Domain models (`LN-History.Model`)

`Channel`, `ChannelUpdate`, `FeePolicy`, `ChannelClosure`, `Node`, `NodeAnnouncement`,
`Address`, `AddressType`, `Block`, `GossipEvent`, plus `Direction` and `ClosureType` enums.
scid carried as `long`; helpers to convert to/from the `BLOCKxTXxOUTPUT` string form.

## 8. API layer (`LN-history.Api`)

### Serialization
- **System.Text.Json**, `JsonNamingPolicy.SnakeCaseLower` (DTO fields are snake_case:
  `scid_str`, `fee_base_msat`, …), `JsonStringEnumConverter` for enums, `byte[]`→base64.
  Drop Newtonsoft. Bulk snapshot endpoints bypass JSON (`application/octet-stream`).
- Mapping domain→DTO done by **hand-written mappers** (conditional expansion logic is
  awkward in AutoMapper).

### DTOs (final agreed shapes)

```
FeePolicyDto { cltv_expiry_delta:int, channel_flags:string(binary),
  fee_base_msat:long, fee_proportional_millionths:long,
  htlc_minimum_msat:long, htlc_maximum_msat:long? }

DirectionPolicyDto { fee_policy:FeePolicyDto?, total_update_count:int }

ChannelUpdateDto { scid:long, scid_str:string, direction:bool,
  source_node_id:string, target_node_id:string, valid_from:DateTime, valid_to:DateTime?,
  fee_policy:FeePolicyDto, timestamp:DateTime, message_flags:string(binary),
  is_topology_update:bool, is_fee_update:bool, gossip_id:string, internal_id:long,
  raw_gossip:byte[]? }

ChannelClosureDto { scid:long, scid_str:string, closure_type:ClosureType(string enum),
  mining_fee:long, txid:string, tx:byte[]? }

ChannelDto { scid:long, scid_str:string, funding_timestamp:DateTime,
  closing_timestamp:DateTime?, closing_information:ChannelClosureDto?, capacity_sat:long,
  node_id_1:string, node_id_2:string, node_1:NodeDto?, node_2:NodeDto?,
  fee_policies: { "0":DirectionPolicyDto, "1":DirectionPolicyDto } | null,
  gossip_id:string, internal_id:long, raw_gossip:byte[]? }

AddressTypeDto { id:int, name:string, description:string }
AddressDto { id:long, network:AddressTypeDto, address:string, port:int }

NodeAnnouncementDto { node_id:string, alias:string, rgb_color:string, features:string,
  addresses:AddressDto[], timestamp:DateTime, is_data_update:bool,
  gossip_id:string, internal_id:long, raw_gossip:byte[]? }

NodeDto { node_id:string, first_seen:DateTime, last_seen:DateTime,
  number_of_channels:int, number_of_channels_all_time:int?, number_of_announcements:int,
  announcements:NodeAnnouncementDto[] }

BlockDto { block_hash:string, block_height:int, timestamp:DateTime,
  space_bytes:long, subsidy_sat:long, tx_fees:long }

GossipEventDto { event_type:"channel"|"channel_update"|"node", timestamp:DateTime, data:object }

PagedResult<T> { items:T[], total:long, limit:int, offset:int }
```

Rules:
- `node_1`/`node_2` populated only when `nodeInformation=true`; `node_id_1/2` always present.
- `raw_gossip` populated only when `raw_gossip=true`; always in schema, null otherwise.
- `fee_policies` populated only on single-channel + history; **omitted in list context**.
  Missing direction → key present with `fee_policy:null, total_update_count:0`.
- `number_of_channels` = currently-open degree (default); `number_of_channels_all_time`
  populated only when `channelCount=all`.
- `NodeDto.announcements` = current active announcement only on `nodes/{id}`; full chain on `/history`.

### Route table (all under `ln-history/v1/`)

**Channel**
- `GET channels/{scid}?nodeInformation&raw_gossip&timestamp` → `ChannelDto` (`{scid}` accepts `865123x1x0` or int)
- `GET channels?timestamp={DateTime|now}&limit&offset` → `PagedResult<ChannelDto>` (no `fee_policies`); no `timestamp`=all history, `now`=open channels
- `GET channels/{scid}/history?raw&timestamp` → `raw=true`→`byte[]`; `raw=false`→ **`ChannelUpdateDto[]`** ordered by `valid_from` *(O1 resolved 2026-07-21: clean update array, not a ChannelDto overload)*
- `GET nodes/{node_id}/channels?timestamp&raw_gossip&limit&offset` → `PagedResult<ChannelDto>`

**Node**
- `GET nodes/{node_id}?raw_gossip&timestamp&channelCount=open|all` → `NodeDto`
- `GET nodes?timestamp={DateTime|now}&limit&offset` → `PagedResult<NodeDto>`
- `GET nodes/{node_id}/history?raw&timestamp` → `raw=true`→`byte[]`; `raw=false`→`NodeDto` (full announcement chain)

**Snapshot**
- `GET snapshot/{timestamp}?withUpdates` → `byte[]` (`application/octet-stream`).
  valid-at-T = open channel_announcements + active node_announcements (+ active channel_updates if `withUpdates`).
- `GET snapshot-diff/{start}/{end}?rawGossip` → `rawGossip=true`→`byte[]`;
  `rawGossip=false`→ `GossipEventDto[]` ordered by timestamp (node events use `NodeAnnouncementDto`).

**Bitcoin**
- `GET blocks/{height:long}` → `BlockDto`
- `GET blocks/{hash}` (constraint `^[0-9a-fA-F]{64}$`) → `BlockDto`
- `GET blocks?timestamp={DateTime}` → `BlockDto` (last block at-or-before T)

**Stats**
- `GET stats/channels/top?by=capacity|update_count|lifetime&limit` → ranked channels
- `GET stats/nodes/top?by=channels|announcements|capacity&limit` → ranked nodes (channels=open degree)
- `GET stats/network?timestamp={DateTime|now}` → network-wide counts
- `GET stats/closures?from&to` → closure counts by type + fee stats

### Auth & errors
- `SimpleApiKeyMiddleware` kept but honors `ApiKeyMiddleware:Enabled` (skip when false).
  401 returns ProblemDetails JSON. **Delete** the SQLite tracking stack
  (`ApiKeyTrackingMiddleware`, `ApiKeyDbContext`, `ApiKeyEntry`, `Migrations/`).
- Errors: 404 + ProblemDetails (missing single resource), 400 + ProblemDetails
  (bad scid/hash/timestamp), empty list → 200 with empty `items`.

## 9. Config

`appsettings`: `ConnectionStrings:PostgreSQL`, `Bitcoind:{RPCHost,RPCPort,RPCUser,RPCPassword}`,
`ApiKey`, `ApiKeyMiddleware:Enabled`. `Fulcrum` section unused (left in place).

## 10. Execution phases

0. **Baseline**: fix `.csproj`s, prune dead packages/projects, get a compiling skeleton.
1. **Model**: domain types + enums.
2. **Data**: `NpgsqlDataSource` + data stores + SQL (EXPLAIN each).
3. **Bitcoin.Data**: RPC client + block store.
4. **Core**: services.
5. **Api**: DTOs, mappers, controllers, serialization, auth, errors, swagger.
6. **Startup**: DI wiring in `Program.cs`.
7. **Verify**: build + drive each endpoint against live DB / RPC.
8. **Cleanup**: delete CouchDB/DuckDB/EF/ApiKey-SQLite; confirm LightningGraph/Cache untouched.

## 11. Risks / cost notes

- **Snapshot at arbitrary T** is the top cost risk (touches 145M `channel_updates` +
  node-announcement history). Must `EXPLAIN`; may need query shaping. Escalate if it
  trips the gate.
- 64-bit `scid` JSON precision → mitigated by always emitting `scid_str`.
- `channel_update_counts` staleness (aggregate) → acceptable per decision.
- All-time node degree ~133K cost/request → opt-in only.

## 12. Future work (out of scope now)

- DB-side `ClosureTypes` lookup table (int→label) + `chain-enricher` update, so
  `closure_type` is normalized in the DB rather than mapped in the API.
- Optional `node_channel_counts` aggregate to make all-time degree cheap.
- Caching layer (`LN-history.Cache`) for expensive snapshot/stats endpoints.
- Consider a `capacity_sat` index on `channels` if top-by-capacity latency matters.
```
