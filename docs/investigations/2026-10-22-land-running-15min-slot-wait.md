# Land Running 15+ minutes: CARD-1495 land waits behind the desktop memory floor (Debug e7503ad9, 2026-10-22)

Read-only Debug. Nothing was changed, cancelled or restarted; the Windows qualification was not touched.
Evidence: desktop database (read-only SQL from the server2 mirror), `GET /api/agent-tasks/a3ba50d2`,
`GET /api/hosts/stats`, `GET /api/hosts/desktop/stats/series?metric=memory&window=30m`, `GET /api/version`,
source at `445df8a80` and the deployed rc tip `0714a9a89`. Observation window 17:37Z to 17:41Z.
The desktop runner's `/build-slots` (:17204) and the desktop server log are not reachable from the
mirror; `http://build-slots:8080/build-slots` is the **server2** broker, which the land does not use.

## Verdict

Not stuck, not leaked: **starved**. This is the CARD-1461 mechanism
([2026-10-22-card-1461-land-verify-memory-floor-starvation.md](2026-10-22-card-1461-land-verify-memory-floor-starvation.md)), recurring.
The land verifier is the only waiter on the desktop build-slot broker, both slots are free, and the
broker refuses every grant because desktop available memory has stayed at 1.2 to 3.2 GB against the
6144 MB floor for the whole wait. The fallback that would bound this (CARD-1461 option C) is deployed
but off. Unless memory recovers, the land times out at about **17:50:48Z** with
`build_slot_timeout` (a `verification_failed` refusal, nothing built, nothing published) and the
caller must re-request it.

## 1. What the land is doing

| Field | Value |
|---|---|
| Request | `acfe5b4a` Running, attempt 1, no hold, `HighestProgress=3`; last writer `monitor-sweep` 17:38:44Z |
| Operation | `0f53af62` Phase 3 (Prepared), Mode Fresh, Publication Unconfirmed |
| Timeline | requested 17:20:10, started 17:20:12, land workspace ready 17:20:44 (`C:\Antiphon\worktrees\land\0f9784a67773`), rebase 17:20:44, prepared 17:20:47 (rebased `ebec1ecf` on target `445df8a8`), **verification started 17:20:48.175**, `VerifiedAt` null |
| Command | `dotnet build; dotnet run --project tests/Antiphon.Tests; policy=diff-requires-build` (no filter) |
| Child process | `ChildProcessId`/`ChildOperation` null; no push, no canonical advance started |

`VerificationStartedAt` is stamped before the slot wait (`AgentTaskLandingProtocol.cs`, per the CARD-1461 note), so the
land is inside `SessionRunnerBuildSlotGate.AcquireAsync` (label `land-verify 0f9784a67773`), polling every 15 s.
It is waiting for a **build slot grant**, refused on **`memory_floor`**, not `busy`.

## 2. Build-slot and lease state now

Desktop broker (server projection `GET /api/hosts/stats` → `desktop.antiphon.buildSlots`, 17:40Z):
`occupied 0 / budget 2, waiters 1, availableMb 1677`; floor is the code default `MinAvailableMemoryMb=6144`
(no desktop override, CARD-1461 Q1). The single waiter is the land (no desktop task holds or waits for a slot: one Check
session in flight). Its queue position is 1; waiting time about 20 minutes at 17:41Z.

Desktop host at 17:40Z: memory used 32.6 of 34.3 GB, available 1.74 GB, **swap/commit 48.0 of 55.9 GB**, CPU 80 %
(5 to 30 minute averages 79 %, max 99 %). Memory series since verification start: 244 samples (17:20:51Z to 17:41:06Z),
available min 1267 MB, max 3179 MB, **0 samples at or above 6144 MB**. The consumer is the Windows rc qualification
(run `d2777e21`, started 16:06:40Z, 8.1 GB free at its start), which runs `dotnet build` and test chunks without
build-slot leases, so it consumes the memory the floor measures while `leases=0` (CARD-1461 Q2).

Not the FIFO class: the land is **not** queued behind review/code builds. server2's broker
(4/4 leases, 4 waiters: `CP-B@bin-c1483-repair`, `build:bin-c1493`, `build:bin-c1448-fix`, `build:bin-c1499b`) is
irrelevant to desktop lands.

Leases: under CARD-1379 the repository mutation lease covers resolution/preparation/rebase only and was released at
about 17:20:47Z; the land does **not** hold it during the slot wait. What it holds is the **land-execution lock** plus
the verification-window marker. Effect:

- Lands: blocked. `16fc75cf` (requested 17:32:27Z) and `5db5df66` (17:33:45Z) are Queued behind it.
- Dispatch cuts: not blocked (`GET /api/agent-tasks/pipeline` shows 0 queued; the window admits distinct managed-worktree claims).
- Ordinary repository writers (settlement syncs, repair/SourceLanding/Interim admission) are fenced by the window marker for its duration.

## 3. Stuck or slow

Neither dead nor leaked: the request row is re-evaluated every sweep (17:37:47Z, 17:38:44Z), the server is live
(`/api/version` 0714a9a89), and the broker shows the waiter. It is starved by a floor that will not clear while the
qualification runs. The gate's deadline is `Landing:BuildSlotWaitMinutes=30`, so it ends by itself at about
17:50:48Z as `build-slot` / `build_slot_timeout position=1` → `verification_failed`, not published.
No recovery action is needed to unstick it; after the refusal the caller re-POSTs `/land/v2` with the same
`expectedSourceSha c526125a…` and `reviewEvidenceId 9e761afb…` once memory recovers (or after the qualification).
Do not cancel the land or restart the AppHost to "free" it: neither frees memory, and a restart mid-qualification
competes for the same RAM.

## 4. Lands today with more than 5 minutes between Running and Landed

From `AgentTaskLandRequests` joined to `AgentTaskLandings` (00:00Z to 17:41Z, 84 requests; normal code-land verify
is 2.5 to 4.5 min; non-code skips under 1 min). "Slot-wait share" = verify minutes above the ~4 min normal build+test.

| Requested (Z) | Task | Running→Landed (min) | Verify (min) | Slot-wait share | Cause |
|---|---|---:|---:|---|---|
| 00:42 | 12653d93 | 5.5 | 4.5 | ~0 | normal build+test |
| 01:19 | ad7cd7b5 | 5.9 | 4.5 | ~0 | normal |
| 03:04 | 1919d384 | 11.4 | 10.4 | ~6-8 min (~65 %) | memory floor by shape (CARD-1461) |
| 03:25 | b23a5273 | 5.4 | 4.5 | ~0 | normal |
| 05:10 | 3b2c2936 | 24.9 | 24.0 | ~20 min (~80 %) | memory floor (CARD-1461); 1 land queued behind (fa175d9b waited 16.6 min) |
| 06:34 | bde3f3ad | 23.3 | 22.5 | ~19 min (~80 %) | memory floor, operator-observed `leases=0`, availableMb 4.7-5.2 GB |
| 08:29 | 36786bbc | 22.8 | 0.9 | 0 | not slots: 21.7 min `repository_or_source_writer` hold before verification; 75c03be9 queued 10.8 min, c80c961f 6.2 min behind it |
| 17:20 | **a3ba50d2** | 21+ running | 20+ running | ~100 % so far | memory floor (this note); 2 lands queued |

So 4 of 8 long lands today (and every one over 10 minutes except 36786bbc) are the memory-floor starvation, each
costing 6 to 20+ minutes plus head-of-line delay for every land behind it. Note: 1919d384/3b2c2936 attribution is by
shape; the proof per land is in the desktop server log (`grep "land-verify" ... | grep "reason=memory_floor"`).

## 5. Is CARD-1461 option C the right remedy, and how is it activated

Correction to the brief: CARD-1461 option C is **not** a land priority slot (that was option B, rejected because the
starvation happens at `leases=0`). Option C is a **bounded memory-floor wait, then an unleased build at a capped
`-maxcpucount`**. It is shipped on the deployed server: `9d5cd02d8 feat(CARD-1461)` is an ancestor of the running rc tip
`0714a9a89` (`LandingSettings.cs` there has the fields). It is off by default.

Activation (operator setting; not changed here):

- `Landing:BuildSlotMemoryFloorFallbackMinutes` = N > 0 (env `Landing__BuildSlotMemoryFloorFallbackMinutes`), optionally
  `Landing:BuildSlotMemoryFloorFallbackMaxCpuCount` (default 2). Must stay below `Landing:BuildSlotWaitMinutes` (30) or it never fires.
- Bound with `services.Configure<LandingSettings>(GetSection("Landing"))` and read through `IOptions<LandingSettings>`
  (`server/Program.cs:482`, `SessionRunnerBuildSlotGate`), which does not reload: it takes effect only after a server
  (AppHost) restart. It cannot rescue the land in flight.
- The verifier then prints `BUILD SLOT unleased reason=memory_floor_fallback available=… floor=… waited=…s maxcpucount=2`
  at Warning in the server log.

Is it right now? It is the right remedy class for the latency (it would have capped today's three 20+ minute stalls),
but the host right now has 1.2-3.2 GB available and 48 GB of commit in swap; an unleased `dotnet build` plus
`Antiphon.Tests` there will page heavily and may take far longer than 4 minutes or squeeze the qualification. A
conservative setting (10 minutes, maxcpucount 2) applied at the next planned restart after the qualification is the
lower-risk choice; option G (raise `BuildSlotWaitMinutes`) only lengthens the tail.

## Recommendation (decision is the operator's)

1. Let the current land time out (~17:50:48Z) or land if memory recovers; then re-POST `/land/v2` for a3ba50d2 with the
   same expected SHA and evidence. Expect `16fc75cf` and `5db5df66` to proceed right after the refusal; if they are
   code lands they hit the same floor.
2. While the Windows qualification runs, either hold code lands (non-code lands are unaffected only when no code land
   is ahead of them) or accept 30-minute timeouts.
3. Enable option C at the next restart after the qualification ends: `Landing__BuildSlotMemoryFloorFallbackMinutes=10`,
   `Landing__BuildSlotMemoryFloorFallbackMaxCpuCount=2`.
4. Follow-ups already identified by CARD-1461 and still open: F (put the last `BUILD SLOT` line into the
   `LandAged` detail so the board says "waiting for memory floor" instead of `no progress`, `holder=unknown`), and
   CARD-0589 Round 2 (qualification/nightly take build slots so the broker sees the consumer).
