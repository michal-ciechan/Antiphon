# CARD-1235: the live-session overlay answers remote sessions from their own runner

Code and landing owner: `0b89ab50-e7a5-423c-9151-39f47e6d3f2f`.
Task base: `78e4001788e99ebc04f0590a84db18442f6324c6` (master at dispatch).

## Today

`AgentService.LoadLiveSessionsAsync` (agent list and agent detail, the board's live-session
source) ends in `AttachRunnerLiveStateAsync`, which copies `TranscriptBinding` and the herdr
fields from one `ISessionRunnerClient.ListAsync`. The injected client is
`RoutingSessionRunnerClient`, whose `ListAsync` reads `_directory.Local` (see
`RoutingSessionRunnerClient.ListAsync`). A session whose row carries `RunnerId`,
`RunnerStoreId` and `RunnerCwd` (a phone-home launch; `PhoneHomeRunnerDirectory.GetBindingAsync`
reads the same three columns) is not in that list, so its overlay fields stay null while a
local session's are filled. The CARD-1228 plan (`a4505a8b1`, its overlay row) records the same
reading. The herdr fields are moot on that lane (phone-home refuses herdr); `TranscriptBinding`
is the field a remote session loses.

The server already has a per-runner list: `ISessionRunnerDirectory.GetInventoryAsync(runnerId)`
returns `RunnerInventory.Available(sessions)` from the connected runner, or `Unavailable(reason)`
when it is not connected, still recovering or lease-expired (`PhoneHomeRunnerDirectory`).
`RunnerSlotService` reads the same inventory.

## Design

Rejected: making `RoutingSessionRunnerClient.ListAsync` merge remote lists. Reconciliation,
zombie census, dispatcher and supervisor callers read it as the local runner's inventory
(`grep -n "ListAsync(" server/Application/Services/*.cs`), so a merge would change what they
decide. The change stays in the overlay.

- S1: `LoadLiveSessionsAsync` keeps no runner data yet; `AgentService` gains an optional
  `ISessionRunnerDirectory? runnerDirectory` constructor parameter (DI supplies the singleton
  registered in `Program.cs`; hand-built harnesses keep null). The failing tests land with it.
- S2: the live-session query also selects the owning runner id when the three binding columns
  are set. `AttachRunnerLiveStateAsync` groups live sessions by owner: the local group reads
  `_runnerClient.ListAsync`, each remote group reads `_runnerDirectory.GetInventoryAsync(owner)`.
  The reads run concurrently under the existing `ListTimeoutSeconds` deadline. A runner's rows
  overlay only the sessions that runner owns. A runner that fails, is unavailable or misses the
  deadline leaves its own sessions' fields null (unknown) and logs; the other groups still apply.
  Without a directory a remote group is left unknown, as today. Cost: an agent list or detail
  read adds one inventory read per remote runner that owns one of its live sessions
  (`PhoneHomeRunnerDirectory.GetInventoryAsync` answers an unavailable runner without a request).

No DTO, client, route or migration change: the client already renders `transcriptBinding`, so the
CARD-0097 / CARD-0506 client work does not collide. Restart: server.

## Verification design

All in `AgentServiceIntegrationTests` (Postgres-backed, `[NotInParallel("AgentQueue")]`), with an
in-test `InventoryDirectory` fake (per-runner Available / Unavailable / hang-until-cancelled
answers, records which runner ids were asked) and a `ListedRunnerClient` for the local list.

- V-1: `C1235_remote_bound_live_session_takes_transcriptBinding_from_its_runners_inventory`:
  agent detail of a remote-bound session whose runner lists it `awaiting-input` reads
  `awaiting-input`, and the directory was asked for that runner only. Red at S1: null.
- V-2: `C1235_local_runner_row_does_not_overlay_a_remote_bound_session`: the local list carries a
  `bound` row under the remote session's id and the owner is Unavailable; the field stays null.
  Red at S1: `bound`.
- V-3: `C1235_agent_list_overlays_local_and_remote_sessions_from_their_own_runners`: the agent list
  shows `awaiting-input` for the local session and `bound` for the remote one. Red at S1: the
  remote field is null.
- V-4: `C1235_hung_remote_runner_leaves_its_session_unknown_and_keeps_the_local_overlay`:
  `ListTimeoutSeconds = 1`, the remote inventory waits for cancellation; the remote runner was
  asked, the local session still reads `awaiting-input`, the remote one null. Red at S1 on the
  asked assertion; the deadline isolation itself is new code with no base behaviour to fail,
  so its defect is PC-3 (disclosure).
- R-1: the full `AgentServiceIntegrationTests` class stays green, including
  `Live_session_dto_carries_transcriptBinding_from_runner_metadata` (local-only mapping).

### Positive controls (pending SourceLanding Mutation)

| PC | Variant | Method-scoped detecting filter |
|---|---|---|
| PC-1 | Skip remote groups (overlay reads the local list only) | `/*/*/AgentServiceIntegrationTests/C1235_remote_bound_live_session_takes_transcriptBinding_from_its_runners_inventory` |
| PC-2 | Apply each runner's rows to every live session regardless of owner | `/*/*/AgentServiceIntegrationTests/C1235_local_runner_row_does_not_overlay_a_remote_bound_session` |
| PC-3 | Let one runner's timeout abandon the whole overlay (return before applying any group) | `/*/*/AgentServiceIntegrationTests/C1235_hung_remote_runner_leaves_its_session_unknown_and_keeps_the_local_overlay` |
| PC-4 | Drop the owner id from the live-session query (every session treated as local) | `/*/*/AgentServiceIntegrationTests/C1235_agent_list_overlays_local_and_remote_sessions_from_their_own_runners` |

Code performs no deliberate mutants.

### Cost

One isolated test build per run plus two rows; a red S1 run (`--rows CP-1`) and a green S2 run
(both rows), within the 90-minute task budget. No whole-Unit lane: the brief excludes it; the
change is confined to `AgentService`'s read-only overlay. Bootstrap the checkpoint tool once
through `scripts/build-slot.ps1` into `bin-c1235-tool/` (the planned unlisted build). Import this
table to an ignored manifest with `timeouts.rowMinutes: 40`; run with `--total-timeout 150m`, the
exact committed `--expected-source-sha`, no `--keep-outputs`, and wait until the exit is not 75.
The red run keeps its outputs; remove only that run's literal output root after inspection.
Each proof selection runs at most three normal times; none after green.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1235-proof/` | overlay | `/*/*/AgentServiceIntegrationTests/C1235_*` | V-1, V-2, V-3, V-4 | 4 tests; S1 4 failed; S2 4 passed | 4 | 10 | false | n/a |
| CP-2 | S1 | CP-1 | agent-service | `/*/*/AgentServiceIntegrationTests/*` | R-1 | S2 0 failed, 0 skipped | 57 | 8 | false | n/a |
