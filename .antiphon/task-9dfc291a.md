# CARD-1124 S3 Final Review (task 9dfc291a)

Reviewed: Code owner `1f89de8f-74e2-4c95-8d4a-73209a512660`, branch `feat/card-task-1f89de8f` at
`845a16d1458b59d0044732b3eb9e624e76d4cdac`, one commit over master `7a03101ba4e8fb7ee49730c5731432baf7406b65`.
`git diff --stat`: 5 files, 21 insertions, 13 deletions, docs and pins only (SKILL.md, orchestration-loop.md,
the CARD-1065 and CARD-1079 plans, `BlockedTaskParkProjectionTests.cs`); no production file. origin/master has
moved to `1364e3814603d137345635381ed9691da10346be` (six commits: CARD-1082 S7 x3, CARD-1105 S3 x3);
`git merge-tree --write-tree origin/master 845a16d14` exit 0, tree `779584cb9`, no conflicting hunk; none of the
five files changed on master since the base. Evidence diff 7a03101ba..845a16d14: commits=1 entries=0 violations=0.

## Verdict: clean, ordinaryScopeCompleted Full

No defect. Every rewritten sentence is true against the code on current master, every flipped or new pin goes
red on a wrong sentence and cannot be satisfied by deleting the sentence or the file, no assertion was weakened
or deleted, and nothing in the instruction files, skills, ops docs, bundles or AGENTS.md tells an operator or
agent to release or count a live Blocked child's seat as free once this lands. The three clauses the Code task
stopped were each a real disagreement with the code; the sentences actually written are true.

## Rerun (one checkpoint-tool run 20261007-043458-cbdf, serial, one isolated build, kept outputs)

Manifest: hand-made five-row table in the scratchpad (the plan's CP-8, the registry guard as CP-12, then the
plan's final rows CP-9..CP-11, which together cover R-1..R-7), `start --plan ... --after S3 --serial
--expected-source-sha 845a16d14 --keep-outputs --total-timeout 90m`. Source clean, `buildSource=verified` on
every row, `unlisted: none`. Build 102 s, wall 5m27s, 114 results. Both validators on the green rows:
`CHECKPOINT SOURCE VALID source=845a16d14 rows=4` (tool `validate --rows CP-8,CP-12,CP-9,CP-10` and
`scripts/validate-checkpoint-receipt.ps1`). Rosters read from each TRX. Evidence:
`.antiphon/checkpoints/20261007-043458-cbdf/report.md` (gitignored, on the mirror under `/work/worktrees/task-9dfc291a`).

| Row | Filter | Executed | Methods | Result |
|---|---|---:|---:|---|
| CP-8 | `/*/*/BlockedTaskParkProjectionTests/*` | 2 | 2 | green (V-7, V-8) |
| CP-12 | `/*/*/(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | 3 | 3 | green (registry guard) |
| CP-9 | plan's seat-final six-class filter | 43 | 32 | green (V-1, V-2, V-5, V-6, V-7, V-8, R-1, R-3, R-4, R-5, R-6); 4+10+10+15+2+2 |
| CP-10 | `/*/*/RunnerSlotEndpointTests/*` | 10 | 10 | green (V-3, V-4, R-2) |
| CP-11 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | 56 | 46 | 55 green, 1 red: CARD-1137 load noise (below) |

CP-11's red, `RunnerSeatOrphanSweepTests.Discovery_request_uses_runner_owned_delivery_evidence`, is a
`TaskCanceledException` in `SessionRunnerHttpClient.ListAsync` from the fixture's `AcquireAsync` inventory read
after 22.7 s with the host at load 12-15 and all 56 methods started together; the same method and shape the
CARD-1124 S1 review filed as CARD-1137. Rerun alone from the kept outputs under slot lease `1499cffe`
(`--treenode-filter "/*/*/RunnerSeatOrphanSweepTests/Discovery_request_uses_runner_owned_delivery_evidence"`):
1/1 passed in 11.3 s (TRX in the scratchpad). S3 changes no file on that path. R-7 is therefore green at this
SHA: 55 in the row plus the one method alone. Disclosure, not a finding; CARD-1137 already exists.

## Code report check

CP-8 2/2 (run 20261007-041508-2537; TRX roster is exactly `C1065_OccupancyTracksProcessesNotBlockedStatus` and
`C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers`, `slot=granted`, `sourceState=clean`, `buildSource=verified`),
registry guard 3/3 (TRX `.antiphon/c1124-s3-guard/run.trx` on the Code mirror: TestClassificationGuardTests 1,
SlowTestTripwireTests 2) and evidence guard 1/0/0 all reproduce. CP-9, CP-10, CP-11 and R-1..R-7 were not run by
Code with a stated reason (the Code brief's closed list); this review ran them. The Code session transcript shows
three leased drivers (`build-slot.ps1 -Label c1124-s3-tool`, `c1124-s3-guard-build`, `c1124-s3-guard-run`) and the
checkpoint tool's own `run --plan` (which leases per build and row); no unleased build or test driver.

## Hard checks

(a) Truth of every rewritten sentence, verified by reading current master.
- Sentence 8 (SKILL.md:25) and sentence 9 (orchestration-loop.md:789): "orphan=true is not a count of free
  seats; it marks a seat with no live desktop session or no owner task, and a live Blocked child reads
  orphan=false with its park field". `RunnerSlotService.IsOrphan` is `!pooledWarm && (!desktopLive || !openTask)`
  (`RunnerSlotService.cs:31-32`); `openTask` is `row.OpenTaskId is not null` (`:45`, `:137`,
  `SeatOccupancySampler.cs:122`); `SeatDesktopJoin.LoadAsync` sets `OpenTaskId` from the latest Queued,
  Dispatched, Working or Blocked task (`SeatDesktopJoin.cs:68-73`); `live` is Created/Starting/Running/Stopping
  (`:139-141`); `RunnerSlotDto.Park` exists (`RunnerSlotDtos.cs:24`, filled at `RunnerSlotService.cs:61`, null
  without a park row). A live Blocked child is therefore `orphan=false` with `park` present (null while parking is
  off). The warm-pool exception is omitted from the short sentence but the sentence only states what orphan=true
  marks, which stays true; ops-http.md:137 carries the full definition.
- Who can release a seat: the single-seat and sweep routes require the operator token
  (`SessionRunnerEndpoints.cs:173-202`, `RequireOperator`); `ReleaseAsync` passes `fromOrphanSweep: false` and
  never reads the flag; `ReleaseOrphansAsync` returns `(0, [])` without the terminal service, which gates on
  `AutomaticEnabled` (default false, `TerminalRunnerSeatReleaseService.cs:17`, `:227`) and additionally on parking
  `Enabled` for a Blocked desktop task (`:61-63`, `:77`). `OccupiesCapacity` is false for Exited and Failed
  (`RunnerSlotService.cs:20-23`), so a process exit frees a seat with no route at all. The reconcile arm reads
  `IsOrphan` only to mark a `pending:orphan:` intent `failed:` (`:133-141`).
- When SeatIdle vs SlotOrphan appears: `BuildSeatIdleItems` needs `Occupies`, an idle class (`IdleBlocked` for a
  Blocked latest task, `SeatOccupancyProjection.cs:38`) and a non-null severity, i.e. age at or above
  `SeatIdleWarningMinutes` (`AttentionService.Seats.cs:18-21`, `SeatOccupancyProjection.cs:54-61`);
  `BuildSlotOrphanItems` needs `seat.Orphan` (`AttentionService.Seats.cs:72`), false for a live Blocked owner.
  `OccupancyDivergence` is host-level and reads `InFlight`, `DispatchedWorking`, `IdleSeats` only (`:37-61`).
- CARD-1065 amendments (D-8, S10, V-25, G-184, PC-184, CP-10): "owner set Queued/Dispatched/Working/Blocked, the
  slot DTO carries `park`, seat evidence carries `park=`" (`ParkField` at `AttentionService.Seats.cs:143-149`,
  appended at `:130` and `:140`); "CP-10 remains the doc-pin row" (its filter is the text-pin class). All true.
- CARD-1079 amendments (ground truth, D-2, D-7, V-6, V-7, V-8): `SeatDesktopJoinTests` has
  `C1124_Join_owner_task_is_queued_dispatched_working_or_blocked` and no longer
  `C1079_Join_open_task_is_dispatched_or_working_only`, while `C1079_Join_reports_latest_bound_task_blocked_at_pooled_warm_and_receipt`
  remains; `SeatOccupancySamplerTests.cs:46` is `OrphanSlots.ShouldBe(1)` and `:82` is `Orphan.ShouldBeFalse()`. True.
- Pin probes from the kept outputs, each reverted (`git checkout --`, tree clean after each, all slot-leased):
  SKILL.md "is not a count" -> "is a count": `c1065-orphan-count` red; orchestration-loop.md same: `c1065-loop-orphan`
  red; SKILL.md with "and count orphan=true before concluding the host is busy" appended: `c1124-skill-no-count`
  red ("should not contain"); orchestration-loop.md with "and count orphan=true" appended: `c1124-loop-no-count` red.

(b) Vacuous satisfaction is impossible. In `C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers` the positive
`Require(..., "orphan=true is not a count of free seats", "c1065-loop-orphan")` precedes
`loop.ShouldNotContain("count orphan=true", ..., "c1124-loop-no-count")` (`BlockedTaskParkProjectionTests.cs:90-91`),
and the skill loop has the same pair (`:112-113`), so a deleted sentence fails the positive pin (probes 1 and 2
above are exactly that case). A deleted file fails `Read`: probe 5 removed SKILL.md and the method failed with
`FileNotFoundException`. The positive phrase itself does not contain the substring `count orphan=true`.

(c) No assertion weakened or deleted: the diff of the test file is the two `Require` phrase changes (labels
`c1065-loop-orphan` and `c1065-orphan-count` kept, as plan D-6 asked) plus the two new `ShouldNotContain` lines;
nothing else moved. `c1124-no-stale-orphan` (`:17`) is unchanged and `docs/session-runtime-invariants.md` is not in
the range.

(d) Stale guidance grep over AGENTS.md, CLAUDE.md, every `.claude/**/*.md`, `server/Bundles/*.md`, `docs/*.md`,
`docs/{adr,investigations,...}/*.md` and `scripts/*` on origin/master: the only "count orphan=true" sentences are
SKILL.md:25 and orchestration-loop.md:789, which this slice rewrites. Every other hit is a different sense of
"orphan" (worktrees, pty hosts, DCP, index.lock, compose) or the release routes' operator-token contract
(ops-http.md:137, docker-stack.md:656, agent-credentials.md:198, `scripts/runner-slots.ps1`), none of which tells
anyone to free or count a live Blocked child's seat. ops-http.md:164 and agent-card-lifecycle.md:13 already say a
live Blocked owner is not an orphan. No remaining stale guidance after land.

(e) Chain completeness. CARD-1124's Ask option 1 is met: S1 (owner predicate, `park` on the slot DTO, owner-doc
pins), S2 (sampler and attention evidence `park=`, api/ops/lifecycle sentences), S3 (instruction sentences, the
`c1065-*` pins kept truthful, dated CARD-1065 and CARD-1079 amendments). What remains after landing S3:
1. Activation: the live desktop server reports `GET /api/version` `5b713f6855ee417737ff7d7e47cb340d66e3d9b8`, 76
   commits behind origin/master and before the S1+S2 landing, and its slots route (read-only GET on server2-temp)
   has no `park` key. The Code report says the batch server restart covers it; until then the live slots route
   still reads the old predicate.
2. SourceLanding Mutation PC-1..PC-11 at the landed SHA (all pending; PC-11 is the owner-doc pin).
3. CARD-1138 (sampler-tick statement pin, cross-task park arm), CARD-1137 (sweep-row load noise), and new
   CARD-1140 (below). None blocks.

## The three stops

1. Plan sentence 9 ended "only the operator release routes free a seat". Dropped; correct. Automatic release
   exists behind `AutomaticEnabled` (plus parking `Enabled` for a Blocked desktop task), a process exit frees a seat
   through `OccupiesCapacity`, and the operator release ignores the orphan flag and is token-gated, so the clause
   was false and would have contradicted the pinned "A Blocked session keeps its runner seat until parking
   releases it". The written sentence ("Answer or cancel each Blocked child.") is true and is the safe instruction.
2. "PC-184's guard is this plan's PC-1" omitted; correct as a stop, because CARD-1065 PC-184 drops Blocked and
   Queued together while PC-1 drops only Blocked and PC-2 only Queued, and PC-184 names a doc-pin method that cannot
   detect a code mutation. But the CARD-1124 plan's Mutation handoff still says "the CARD-1065 plan amendment in S3
   says so", and the PC-184 row now points at the CARD-1124 plan without naming PC-1+PC-2. Disclosure: CARD-1140.
3. CARD-1079 amendment: "SeatIdle only" became "SeatIdle from the warning age and not SlotOrphan" (below the
   warning age there is no SeatIdle row, and host-level OccupancyDivergence can still fire), and "V-6 is replaced"
   became "the open-task method named by V-6 is replaced" (the pooled-warm method remains). Both corrections are
   true. Nit, no card: the same five-clause amendment is pasted verbatim into six places of the CARD-1079 plan,
   including each of the V-6, V-7 and V-8 rows, where one clause per row would have read better.

## Disclosures

- CARD-1140 (Backlog, filed by this review): amend the CARD-1065 PC-184/G-184 rows and the CARD-1124 Mutation
  handoff to say PC-1 and PC-2 together are PC-184's executed guard, and carry that note into the Mutation brief.
- CP-11 as one row is load-sensitive (CARD-1137): red in-row, green alone, twice now across reviews.
- S1+S2 not yet activated on the live server (see (e) item 1).
- Delivery/queue/receipt audit from the stage bundle: not applicable, the slice changes no producer, recipient,
  storage or receipt path and no session is dispatched by it.

## Platform and process

`GET /api/runner-defaults`: global runner server2, no kind defaults. `GET /api/session-runners`: desktop windows
0/2, server2 linux 0/10 draining, server2-temp linux 3/10. No runner or platform pin. Build-slot broker budget 4,
one lease occupied at start. Every build and run took a slot: the driver build, the five rows, the method rerun
and the five probes (twelve leases, all `waited=0s`). `clean --run 20261007-043458-cbdf` removed the kept
`bin-c1124r3/` outputs; the driver `tools/Antiphon.Checkpoints/bin-c1124r3-drv` was removed with a root-confined rm;
no `bin-c1124*` directory remains; the tree is clean at `845a16d14`. No file in the repository was changed by this
review other than this report. PC-1..PC-11 stay pending for method-scoped SourceLanding Mutation.

--- review evidence ---
subjectTaskId: 1f89de8f-74e2-4c95-8d4a-73209a512660
reviewedSourceSha: 845a16d1458b59d0044732b3eb9e624e76d4cdac
reviewedSourceClean: true
ordinaryScopeCompleted: Full
