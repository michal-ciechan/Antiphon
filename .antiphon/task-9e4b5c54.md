# CARD-0519 S12g — Code task 9e4b5c54

S12g passed: CP-74 and CP-75 executed 21 results, all passed, zero failed or
skipped. No production, test, configuration or plan change was needed.

Original Code task / landing owner: `9e4b5c54-0b07-4d78-8f5f-dc722c47d899`.
Branch: `feat/card-task-9e4b5c54`.
Worktree: `/work/worktrees/task-9e4b5c54`.
Task base and actual tested source: `cca0a1756be64b97ea4abea8042a8a60cc030b73`.
Only this Markdown report is committed after qualification. Its later commit is
not the tested SHA; no receipt is relabelled to that commit.

Plan: `docs/superpowers/plans/2026-10-04-card-0519-unified-outbound-recovery-plan.md`,
section `S12 split selection (Plan 02c16198, 2026-10-05)`, main `### Checkpoints`
table, and `### S12 checkpoint groups`, as present at the tested source.

## Scope and results

| Checkpoint | Invariant | Executed | Passed | Failed | Skipped | Test-host time |
|---|---|---:|---:|---:|---:|---:|
| CP-74 | V-11: death before capture, after capture, during partial staging, and after complete staging recovers the original adapter receipt without a new wake event | 12 | 12 | 0 | 0 | 378.9400454s |
| CP-75 | V-12: death during publication remains uncertain without automatic replay; acknowledged retry works; committed Published never replays | 9 | 9 | 0 | 0 | 634.1633942s |

Together the rows execute the full `ChannelOutboundUnifiedCrashTests` class:
seven methods, each with main/trailing/machine arguments. CP-75's nine native
results additionally exercise eighteen internal path/cut combinations: three
attempt boundaries, two acceptance/outcome boundaries, and one Published boundary
for each path. Internal combinations are not counted as extra TUnit results.
The receiver assertions check exact Slack conversation/thread/text and original
attachment bytes. Independent database reloads check ownership, attempts,
settlement, visible failure, and projections. Owned children are killed and
awaited at observed barriers and fresh children perform recovery.

The commissioned round is Final profile v1, with the explicit checkpoint-only
S12g scope from the brief and D-S12-7/D-S12-9. S12g is independent of S12f; no
S12f output was consumed. No whole-Unit, full-assembly, baseline sweep or unlisted
application test ran. There are no new tests here, so the red-first baselines
remain with S12c/S12d under D-S12-10. No deliberate mutant was introduced.
There was one ordinary execution per selected case, zero reruns and zero repair
rounds. No timeout, assertion, filter or retry setting was changed.

| Verification ID | Actual outcome in this task |
|---|---|
| V-1 | Not run; earlier schema slice, outside S12g |
| V-2 | Not run; earlier capture slice, outside S12g |
| V-3 | Not run; earlier materialization slice, outside S12g |
| V-4 | Not run; earlier unified-path slice, outside S12g |
| V-5 | Not run; earlier discovery slice, outside S12g |
| V-6 | Not run; earlier trailing-recovery slice, outside S12g |
| V-7 | Not run; earlier retry slice, outside S12g |
| V-8 | Not run; earlier failure-recording slice, outside S12g |
| V-9 | Not run; earlier metadata-repair slice, outside S12g |
| V-10 | Not run; earlier retention slice, outside S12g |
| V-11 | Passed, CP-74: 12/12 |
| V-12 | Passed, CP-75: 9/9 |
| V-13 | Not run; S12f owns CP-70/CP-77/CP-71 |
| R-1 | Not run; outside S12g |
| R-2 | Not run; outside S12g |
| R-3 | Not run; outside S12g |
| R-4 | Not run; outside S12g |
| R-5 | Not run; S12e owns the final delivery-class selection |
| R-6 | Not run; outside S12g |
| R-7 | Not run; outside S12g |
| R-8 | Not run; S12e owns the final full recovery class and named methods |
| R-9 | Not run; S12e owns the final composed transport class |
| R-10 | Not run; outside S12g |
| R-11 | Not run; outside S12g |
| R-12 | Not run; S13 owns CP-29 on Windows |

Earlier evidence retains its original attribution; none of those rows is claimed
rerun here. Caller-owned whole-Unit qualification under D-S12-7, S12f's result,
S13 documentation and Windows R-12, and all post-land controls remain separate
release obligations. This is not an Interim round and defers no assigned S12g
row. There is no live manual acceptance assigned to S12g. CP-75 exercises explicit
acknowledged recovery in isolated fixtures; no production recovery action ran.

`ChannelOutboundSettings.UnifiedRecoveryEnabled` remains false by default.
No deployed configuration was changed. Restart: **none**. The caller/orchestrator
owns later server activation after every D-S12-8 gate, Review, landing and
SourceLanding Mutation.

The S12e report at `.antiphon/task-11c3a54a.md` qualifies
`b4be3d4665ea989da3c3c0e4a5fc414bda7cd7c4`, whereas this brief explicitly selected
`cca0a1756be64b97ea4abea8042a8a60cc030b73`. The crash class, transport fixture,
probe and outbound pump are unchanged across those commits, but other repository
source changed. These are distinct SHA receipts, not proof that all final groups
ran at one identical repository SHA. Caller/Review owns reconciliation of the
D-S12-8/D-S12-9 common-candidate gate; this task did not broaden its closed list.

## Execution and provenance

Read `GET /api/runner-defaults` and `GET /api/session-runners` before execution.
Defaults revision 2 named server2; the catalogue showed desktop and both Linux
entries, with server2 draining and server2-temp accepting new work. These were
observations, not embedded placement settings. No Runner or Platform pin was used.

The sole additional build was the required checkpoint-tool bootstrap:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c519-s12g-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-s12g-tool/ --nologo
```

It passed with zero errors and one existing CS8602 warning at
`TaskOwnerGuard.cs:170`: slot=granted waited=0s, maxcpucount=6, lease held 11s.
The manifest build passed with zero errors and 621 warnings in 167.045943s,
also slot=granted waited=0s. The tool imported 64 build definitions, but 63 were
unused: only CP-74's `bin-c519-cp74/` build executed; CP-75 reused its certified
output as the amended S12g manifest requires. No process-wide OutputPath override
was set. Output selection was left to the checkpoint tool's manifest binding.

```sh
dotnet tools/Antiphon.Checkpoints/bin-s12g-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-04-card-0519-unified-outbound-recovery-plan.md --after S12g --expected-source-sha cca0a1756be64b97ea4abea8042a8a60cc030b73 --row-timeout 15m --total-timeout 60m --serial --max-wait 50s
dotnet tools/Antiphon.Checkpoints/bin-s12g-tool/Antiphon.Checkpoints.dll wait --run 20261006-021046-294b --max-wait 50s
```

Wait continued after every exit 75 until final exit 0. No run remains in flight.
Both rows used `TUNIT_MAX_PARALLEL_TESTS=1`, slot=granted waited=0s. Total checkpoint
wall time was 1181.7799431s (19m41s), within the approximately 26-minute estimate.
Both rows retained their 15-minute cap and the group its 60-minute cap.

Fresh TRX counters, test definitions, class/method rosters and argument display
names were inspected for both rows. All intended methods had three passed results,
with no unexpected classes and no failures/skips. Both receipts have dirty=0,
sourceState=clean, buildSource=verified and the exact expected committed SHA.
The schema-2 receipt validator returned exit 0:

```text
CHECKPOINT SOURCE VALID source=cca0a1756be64b97ea4abea8042a8a60cc030b73 rows=2
```

```sh
pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261006-021046-294b/report.json -ExpectedSourceSha cca0a1756be64b97ea4abea8042a8a60cc030b73 -Rows CP-74,CP-75
```

Raw evidence root:
`/work/worktrees/task-9e4b5c54/.antiphon/checkpoints/20261006-021046-294b/`.
It retains report.json/report.md, executor/build/row logs and both fresh run.trx
files as ignored generated evidence. Only this individual Markdown report is
committed. The tool removed its produced test output and dead shadow copy after
green; the separately owned bootstrap output is removed before settlement.

The full task-history guard, including this report-only commit, is run before
settlement; its exact result and pushed commit are given in the final task response:

```sh
pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef cca0a1756be64b97ea4abea8042a8a60cc030b73 -HeadRef HEAD
```

## Unedited checkpoint lines

```text
CHECKPOINT CP-74 commit=cca0a1756be64b97ea4abea8042a8a60cc030b73 build=ok filter=/*/*/ChannelOutboundUnifiedCrashTests/(C519_Before_capture_death_is_discovered*)|(C519_Captured_death_needs_no_wake_signal*)|(C519_Partial_stage_death_retries_preparation*)|(C519_Complete_stage_death_preserves_snapshot*) executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-9e4b5c54/.antiphon/checkpoints/20261006-021046-294b/rows/CP-74/run.trx slot=granted waited=0s dirty=0 source=cca0a1756be64b97ea4abea8042a8a60cc030b73 sourceState=clean buildSource=verified
CHECKPOINT CP-75 commit=cca0a1756be64b97ea4abea8042a8a60cc030b73 build=reused filter=/*/*/ChannelOutboundUnifiedCrashTests/(C519_Attempt_death_stays_uncertain*)|(C519_Accepted_death_stays_uncertain*)|(C519_Published_death_never_replays*) executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-9e4b5c54/.antiphon/checkpoints/20261006-021046-294b/rows/CP-75/run.trx slot=granted waited=0s dirty=0 source=cca0a1756be64b97ea4abea8042a8a60cc030b73 sourceState=clean buildSource=verified
```

## Fresh TRX roster

All entries are in Antiphon.Tests.Application.ChannelOutboundUnifiedCrashTests.

| Checkpoint | Native result | Actual outcome |
|---|---|---|
| CP-74 | C519_Before_capture_death_is_discovered(main) | Passed |
| CP-74 | C519_Before_capture_death_is_discovered(trailing) | Passed |
| CP-74 | C519_Before_capture_death_is_discovered(machine) | Passed |
| CP-74 | C519_Captured_death_needs_no_wake_signal(main) | Passed |
| CP-74 | C519_Captured_death_needs_no_wake_signal(trailing) | Passed |
| CP-74 | C519_Captured_death_needs_no_wake_signal(machine) | Passed |
| CP-74 | C519_Partial_stage_death_retries_preparation(main) | Passed |
| CP-74 | C519_Partial_stage_death_retries_preparation(trailing) | Passed |
| CP-74 | C519_Partial_stage_death_retries_preparation(machine) | Passed |
| CP-74 | C519_Complete_stage_death_preserves_snapshot(main) | Passed |
| CP-74 | C519_Complete_stage_death_preserves_snapshot(trailing) | Passed |
| CP-74 | C519_Complete_stage_death_preserves_snapshot(machine) | Passed |
| CP-75 | C519_Attempt_death_stays_uncertain(main) | Passed |
| CP-75 | C519_Attempt_death_stays_uncertain(trailing) | Passed |
| CP-75 | C519_Attempt_death_stays_uncertain(machine) | Passed |
| CP-75 | C519_Accepted_death_stays_uncertain(main) | Passed |
| CP-75 | C519_Accepted_death_stays_uncertain(trailing) | Passed |
| CP-75 | C519_Accepted_death_stays_uncertain(machine) | Passed |
| CP-75 | C519_Published_death_never_replays(main) | Passed |
| CP-75 | C519_Published_death_never_replays(trailing) | Passed |
| CP-75 | C519_Published_death_never_replays(machine) | Passed |

## Positive-control disposition

Every ID below and every variant defined for it in the plan remains pending
method-scoped SourceLanding Mutation. Ordinary green discharges none. Mutation
owns deliberate mutants, assertion-red/restore/green, and missing-control discovery.

For this slice specifically, PC-89..PC-95 remain pending for each main, trailing
and machine path. PC-94 includes publishing-committed, before-producer-call and
producer-entered cuts; PC-95 includes producer-accepted and outcome-refused;
PC-89 uses published-committed. PC-90/91/92/93 respectively use capture-before-commit,
capture-committed, input-temporary-partial and input-stage-complete. These are
ordinary witnesses now, not completed positive controls.

| PC / named variant | Status |
|---|---|
| PC-1 | Pending SourceLanding Mutation |
| PC-2 | Pending SourceLanding Mutation |
| PC-3 | Pending SourceLanding Mutation |
| PC-4 | Pending SourceLanding Mutation |
| PC-5 | Pending SourceLanding Mutation |
| PC-6 | Pending SourceLanding Mutation |
| PC-7 | Pending SourceLanding Mutation |
| PC-8 | Pending SourceLanding Mutation |
| PC-9 | Pending SourceLanding Mutation |
| PC-10 | Pending SourceLanding Mutation |
| PC-11 | Pending SourceLanding Mutation |
| PC-12 | Pending SourceLanding Mutation |
| PC-13 | Pending SourceLanding Mutation |
| PC-14 | Pending SourceLanding Mutation |
| PC-15 | Pending SourceLanding Mutation |
| PC-16 | Pending SourceLanding Mutation |
| PC-17 | Pending SourceLanding Mutation |
| PC-18 | Pending SourceLanding Mutation |
| PC-19 | Pending SourceLanding Mutation |
| PC-20 | Pending SourceLanding Mutation |
| PC-21 | Pending SourceLanding Mutation |
| PC-22 | Pending SourceLanding Mutation |
| PC-23 | Pending SourceLanding Mutation |
| PC-24 | Pending SourceLanding Mutation |
| PC-25 | Pending SourceLanding Mutation |
| PC-26 | Pending SourceLanding Mutation |
| PC-27 | Pending SourceLanding Mutation |
| PC-28 | Pending SourceLanding Mutation |
| PC-29 | Pending SourceLanding Mutation |
| PC-30 | Pending SourceLanding Mutation |
| PC-31 | Pending SourceLanding Mutation |
| PC-32 | Pending SourceLanding Mutation |
| PC-33 | Pending SourceLanding Mutation |
| PC-34 | Pending SourceLanding Mutation |
| PC-35 | Pending SourceLanding Mutation |
| PC-36 | Pending SourceLanding Mutation |
| PC-37 | Pending SourceLanding Mutation |
| PC-38 | Pending SourceLanding Mutation |
| PC-39 | Pending SourceLanding Mutation |
| PC-40 | Pending SourceLanding Mutation |
| PC-41 | Pending SourceLanding Mutation |
| PC-42 | Pending SourceLanding Mutation |
| PC-43 | Pending SourceLanding Mutation |
| PC-44 | Pending SourceLanding Mutation |
| PC-45 | Pending SourceLanding Mutation |
| PC-46 | Pending SourceLanding Mutation |
| PC-47 | Pending SourceLanding Mutation |
| PC-48 | Pending SourceLanding Mutation |
| PC-49 | Pending SourceLanding Mutation |
| PC-50 | Pending SourceLanding Mutation |
| PC-51 | Pending SourceLanding Mutation |
| PC-52 | Pending SourceLanding Mutation |
| PC-53 | Pending SourceLanding Mutation |
| PC-54 | Pending SourceLanding Mutation |
| PC-55 | Pending SourceLanding Mutation |
| PC-56 | Pending SourceLanding Mutation |
| PC-57 | Pending SourceLanding Mutation |
| PC-58 | Pending SourceLanding Mutation |
| PC-59 | Pending SourceLanding Mutation |
| PC-60 | Pending SourceLanding Mutation |
| PC-61 | Pending SourceLanding Mutation |
| PC-62 | Pending SourceLanding Mutation |
| PC-63 | Pending SourceLanding Mutation |
| PC-64 | Pending SourceLanding Mutation |
| PC-65 | Pending SourceLanding Mutation |
| PC-66 | Pending SourceLanding Mutation |
| PC-67 | Pending SourceLanding Mutation |
| PC-68 | Pending SourceLanding Mutation |
| PC-69 | Pending SourceLanding Mutation |
| PC-70 | Pending SourceLanding Mutation |
| PC-71 | Pending SourceLanding Mutation |
| PC-72 | Pending SourceLanding Mutation |
| PC-73 | Pending SourceLanding Mutation |
| PC-74 | Pending SourceLanding Mutation |
| PC-75 | Pending SourceLanding Mutation |
| PC-76 | Pending SourceLanding Mutation |
| PC-77 | Pending SourceLanding Mutation |
| PC-78 | Pending SourceLanding Mutation |
| PC-79 | Pending SourceLanding Mutation |
| PC-80 | Pending SourceLanding Mutation |
| PC-81 | Pending SourceLanding Mutation |
| PC-82 | Pending SourceLanding Mutation |
| PC-83 | Pending SourceLanding Mutation |
| PC-84 | Pending SourceLanding Mutation |
| PC-85 | Pending SourceLanding Mutation |
| PC-86 | Pending SourceLanding Mutation |
| PC-87 | Pending SourceLanding Mutation |
| PC-88 | Pending SourceLanding Mutation |
| PC-89 | Pending SourceLanding Mutation |
| PC-90 | Pending SourceLanding Mutation |
| PC-91 | Pending SourceLanding Mutation |
| PC-92 | Pending SourceLanding Mutation |
| PC-93 | Pending SourceLanding Mutation |
| PC-94 | Pending SourceLanding Mutation |
| PC-95 | Pending SourceLanding Mutation |
| PC-96 | Pending SourceLanding Mutation |
| PC-97 | Pending SourceLanding Mutation |
| PC-98 | Pending SourceLanding Mutation |
| PC-99 | Pending SourceLanding Mutation |
| PC-100 | Pending SourceLanding Mutation |
| PC-1059-1 / roots | Pending SourceLanding Mutation |
| PC-1059-2 / traversal | Pending SourceLanding Mutation |
| PC-1059-3 / file and directory links | Pending SourceLanding Mutation |
| PC-1059-4 / pre-read budget | Pending SourceLanding Mutation |
| PC-1059-5 / Linux regular type | Pending SourceLanding Mutation |
| PC-1059-6 / Linux growing-file budget | Pending SourceLanding Mutation |
| PC-1..PC-5 (S methods; schema regeneration and fresh migration each phase) | Pending SourceLanding Mutation |
| PC-89..PC-95 (X methods; three path results plus internal crash cuts per phase) | Pending SourceLanding Mutation |
| PC-96 (W.C519_Converter_handoff; real queue/broker/recipient per phase) | Pending SourceLanding Mutation |
| PC-6..PC-88 and PC-97..PC-100 (the remaining exact methods) | Pending SourceLanding Mutation |
| PC-S4-1 / default off | Pending SourceLanding Mutation |
| PC-19 / S4 main and machine activation | Pending SourceLanding Mutation |
| PC-15 / S4 staged source | Pending SourceLanding Mutation |
| PC-22 / S4 silence | Pending SourceLanding Mutation |
| PC-23 / S4 origin | Pending SourceLanding Mutation |
| PC-S4-2 / catalog-less main | Pending SourceLanding Mutation |
| PC-S4-3 / catalog-less machine | Pending SourceLanding Mutation |
| PC-S4-4 / catalog-less trailing | Pending SourceLanding Mutation |
| PC-S5-1 / event closure | Pending SourceLanding Mutation |
| PC-S5-2 / transactional closure | Pending SourceLanding Mutation |
| PC-S5-3 / complete machine batch | Pending SourceLanding Mutation |
| PC-S5-4 / original context time | Pending SourceLanding Mutation |
| PC-S6-1 / silent main root | Pending SourceLanding Mutation |
| PC-S6-2 / root fair budget | Pending SourceLanding Mutation |
| PC-S6-3 / machine trailing policy | Pending SourceLanding Mutation |
| PC-S6-4 / trailing API withholding | Pending SourceLanding Mutation |
| PC-S6-5 / terminal ordering | Pending SourceLanding Mutation |
| PC-S7-1 / null accepted lease | Pending SourceLanding Mutation |
| PC-S8-1 / SentAt origin | Pending SourceLanding Mutation |
| PC-S8-2 / recording-only preparation repair | Pending SourceLanding Mutation |
| PC-S12-1 | Pending SourceLanding Mutation |
| PC-S12-2 | Pending SourceLanding Mutation |
| PC-S12-3 | Pending SourceLanding Mutation |
| PC-S12-4 | Pending SourceLanding Mutation |
| PC-S12-5 | Pending SourceLanding Mutation |
| PC-S6-2 / reset root cursor | Pending SourceLanding Mutation |
| PC-S6-2 / remove Take(PageSize) | Pending SourceLanding Mutation |
| PC-S6-2 / remove MaximumPages | Pending SourceLanding Mutation |
| PC-45 / owner, version, expiry and state variants | Pending SourceLanding Mutation |
| PC-S9-1 / reset repair cursor | Pending SourceLanding Mutation |
| PC-S9-1 / remove repair maximum-pages bound | Pending SourceLanding Mutation |
| PC-S10-1a / EOF | Pending SourceLanding Mutation |
| PC-S10-1b / partial native file | Pending SourceLanding Mutation |
| PC-S10-1c / malformed native file | Pending SourceLanding Mutation |
| PC-S10-1d / file identity | Pending SourceLanding Mutation |
| PC-S10-1e / exit grace | Pending SourceLanding Mutation |
| PC-S10-2a / terminal generation | Pending SourceLanding Mutation |
| PC-S10-2b / persisted payload | Pending SourceLanding Mutation |
| PC-S10-2c / original prompt membership | Pending SourceLanding Mutation |
| PC-S10-3a / runtime flag or generation | Pending SourceLanding Mutation |
| PC-S10-3b / HTTP flag or generation | Pending SourceLanding Mutation |
| PC-S10-3c / phone-home flag or generation | Pending SourceLanding Mutation |
| PC-84 / Published root | Pending SourceLanding Mutation |
| PC-84 / Suppressed root | Pending SourceLanding Mutation |
| PC-84 / ineligible machine source | Pending SourceLanding Mutation |
| PC-81 / prior channel context and classification | Pending SourceLanding Mutation |
| PC-S12-5 / C519_Queue_to_adapter | Pending SourceLanding Mutation |
| PC-S12-5 / C519_Queue_to_busy_adapter | Pending SourceLanding Mutation |

## Handoff

Next: Review of S12g receipts and this Markdown-only change. Preserve original
Code task 9e4b5c54 as landing owner. Caller lands after Review and commissions
SourceLanding Mutation; reconcile the common-candidate gate before activation.
S12f and caller-owned Unit/S13 qualification remain outside this completed slice.
Restart: none; activation owner: caller/orchestrator.
