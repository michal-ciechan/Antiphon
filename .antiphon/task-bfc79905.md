# CARD-0667 S3h Code checkpoint — incomplete

The server implementation and three runtime-backed witnesses are pushed, but CP-21 is red. The final run built successfully and executed all three intended methods; each failed in the new phone-home fixture because its connection was never marked recovered. The brief's maximum two repair rounds is exhausted. This is not ready for Review or landing; next is Code.

## Identity and scope

- Original Code task / eventual landing owner: `bfc79905-468d-4c41-91f7-88871f55e568`.
- Branch: `feat/card-task-bfc79905`; worktree: `/work/worktrees/task-bfc79905`.
- Task base: `a2467393702da9c8cffea1b755ac93c0cb1aaa03` (S3g).
- Actual final tested implementation SHA: `ca9ff7f3dc03653a4eefc2a29c51c03356a6982b`. This report is a subsequent documentation-only commit; its SHA is reported to the caller separately. No receipt is relabelled as testing that report commit.
- Plan: `docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md`, S3h / CP-21 / G-100–102 / PC-100–102.
- Followed the brief's specific closed CP-21 scope and explicit prohibition on whole Unit runs. No whole assembly/class battery was run or claimed; no unbounded shared-impact qualification was attempted.
- `ANTIPHON_TASK_TOKEN` was present. Authenticated GET `/api/runner-defaults` and `/api/session-runners` succeeded; defaults revision 2, available Linux and Windows entries. No runner/platform pin or deployment setting was changed.
- S3h remains based on the assigned S3g branch and must eventually land by adoption after S3g publication. No rebase, reset, amend or force push occurred.

## Changed files and behavior

1. `server/Application/Services/TerminalRunnerSeatReleaseService.cs`: adds typed `TerminalRunnerSeatEvidence` and `ObserveCapturedCandidateAsync`. It constructs captured-mode requests from inventory identity with empty binding / floor -1, requires both capability flags, checks runner availability/store identity, and returns typed holds for missing, malformed, lost or unsupported evidence. It reads no session/queue row to manufacture a floor. Captured reservations/reconciliation use this boundary; dispatch reacquires fresh matching proof rather than trusting a durable token alone. Legacy explicit-floor handling remains separate.
2. `tests/Antiphon.Tests/Application/RunnerSeatReleaseFixture.cs`: adds opt-in real runner, provider tailers, fake child, loopback production HTTP mapper and WebSocket dispatcher/PhoneHomeRuntimeAdapter, production server clients, independent fake runner/server clocks, actual runtime inventory and server/runner restart composition. Fixed qualified responses throw if reached in this mode. Native prompt/end records follow actual submitted child bytes. The phone-home recovery setup is incomplete as detailed below.
3. `tests/Antiphon.Tests/Application/RunnerSeatOrphanSweepTests.cs`: adds the plan's three exact methods. Scenarios cover rowless HTTP/phone-home acquisition, Claude/Grok/Codex old-generation refusal and Enter retry, durable known-attempt reservation/server restart/runner restart, missing/invalid evidence, unsupported peers, malformed/lost replies, unavailable/stale/adopting/store-mismatched peers and a valid control.

`AutomaticEnabled=false` remains the production default. No migration, discovery iteration, timer/job hookup, activation, native provider launch, force-release fallback, timeout widening or assertion relaxation was introduced.

## Verification outcomes

Only CP-21 was selected, always through the checkpoint tool with the then-committed HEAD as `--expected-source-sha`. Every command was awaited. Every build/test lease was granted with `waited=0s`; the first failed-build row itself correctly reports `slot=skipped` because its test driver never launched.

| Attempt | SHA | Outcome |
|---|---|---|
| `20261005-154244-f6a0` | `185e87f5e9fae7571186eace4961d35e29d7e9c6` | Build failed: ambiguous HostStatsSettings in the new fixture. 0 tests; not defect-detection evidence. Repair 1 qualified the runner type. |
| `20261005-154716-35be` | `745ea65a7c82198df63793952700769ff6657c82` | Build passed; 3 executed, 0 passed, 3 failed, 0 skipped. All failed on fixture HttpClient disposal. Repair 2 gave factory-created read clients independent ownership. Not defect-detection evidence. |
| `20261005-155334-14be` | `3087478c04e68fd445e8c64366769f7fb2a58ab4` | Compiling intended red: 3 executed, 0 passed, 3 failed, 0 skipped. Named G-100 missing request, G-101 Unsupported instead of Waiting, G-102 Unsupported instead of Unknown against the preimplementation boundary. |
| `20261005-155924-f90b` | `ca9ff7f3dc03653a4eefc2a29c51c03356a6982b` | Implementation build passed; 3 executed, 0 passed, 3 failed, 0 skipped. All three fail at phone-home input admission. No green claim. |

Fresh TRX was inspected for all three exact methods in the intended-red and final runs. Their final outcomes are all **Failed**:

- `Discovery_request_uses_runner_owned_delivery_evidence`
- `Server_restart_reacquires_runner_delivery_evidence`
- `Evidence_missing_or_peer_unsupported_defers_discovery`

The final failure is `InvalidOperationException: Phone-home connection is not dispatch-eligible`, through `PhoneHomeLiveConnection.RequestAsync` -> `PhoneHomeRunnerClient.SendInputAsync` -> `LiveSeat.SubmitAsync`. These are new fixture failures, not alleged inherited red. Source state was clean and buildSource verified for all three executed runs. `validate-checkpoint-receipt.ps1` on the final report and exact tested SHA returned exit 2, `CHECKPOINT SOURCE INVALID reason=row_failed`, as expected for red; no eligible green receipt exists.

The one separately declared setup build was checkpoint-tool bootstrap through `scripts/build-slot.ps1`, label `c667-s3h-tool-bootstrap`, alternate `bin-c667-tool/`, `UseAppHost=false`: succeeded, `slot=granted waited=0s`, held 9s. There were no other unlisted build/test commands. There were three actual test-host executions, no loaded repetitions and no deliberate mutants.

V/R ledger: **V-3 S3h subset FAILED** (CP-21, three methods above); remaining V-3 methods are not certified here. V-1, V-2 and R-1, R-2, R-3, R-4 were not selected by this slice's brief, and are not marked passed. Remaining ordinary CP-16/17/18 and final CP-1–6 stay with their planned slices. Whole Unit / full-class Final scope is not claimed. Manual operational activation acceptance was not run; this is dormant development only. The required defaults/catalogue inspection passed.

All PC-100/101/102 variants remain **pending for post-land SourceLanding Mutation**, including both transport variants, the PC-100 provider/old-generation scenarios, PC-101 server-vs-runner restart distinction, and PC-102 release-only capability control. No other plan PC (PC-1–104) is discharged by this task. Mutation owns deliberate red/restore/green and discovery.

## Exact remaining work

`LiveSeat.StartTransportAsync` creates the production `PhoneHomeRunnerClient` after `WaitLiveAsync`, but does not complete the host directory's recovery barrier. `PhoneHomeTestHost` deliberately leaves newly connected peers in that state. `PhoneHomeConnectionTests.Recovery_barrier_withholds_dispatch` demonstrates the existing `host.Directory.MarkRecovered(live)` seam, and `PhoneHomeLiveConnection.RequestAsync` correctly refuses Input until it is recovered.

The next Code commission should complete a real inventory List through the new dispatcher/client and mark that exact current connection recovered before input is used, including every recreated server/runner transport. Keep this change inside RunnerSeatReleaseFixture.cs; do not weaken the production recovery gate or fake qualified observations. Re-run the exact closed CP-21 selection on its committed fix and inspect all three methods. No third repair was attempted in this dispatch. Additional failures after that fix remain unknown.

Rerun after committing and pushing the repair (bootstrap the tool under the build-slot gate because outputs were cleaned):

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c667-s3h-tool-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c667-tool/ --property:UseAppHost=false
$sha = git rev-parse HEAD
dotnet run --no-build --no-restore --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c667-tool/ --property:UseAppHost=false -- run --plan docs/superpowers/plans/2026-10-01-card-0667-terminal-task-seat-release-plan.md --rows CP-21 --expected-source-sha $sha --max-wait 50s
```

Follow emitted wait commands until exit is not 75. No Review/land until ordinary evidence is green. Preserve the original Code landing owner named above.

## Cleanup and provenance

Checkpoint cleanup for final run `20261005-155924-f90b` removed all 28 owned `bin-c667-s3h` project output directories, also used by the earlier attempts. The separate `tools/Antiphon.Checkpoints/bin-c667-tool` setup output was removed afterward. No project alternate build output remains; checkpoint build-log directories named `bin-c667-s3h` remain as evidence. All checkpoint executors were awaited; wait removed their shadow tool copies. Generated JSON/TRX/log payloads remain ignored under `.antiphon/checkpoints/`; only this Markdown evidence summary is committed.

`scripts/check-evidence-diff.ps1` passed over task base through the implementation SHA (4 commits, 0 violations). It is run again over the full base..final report HEAD before settlement; the caller summary records that result. `git diff --check` passed.

Restart: **none performed**; eventual server activation/restart belongs to the caller's separate commission after completed Code/Review/publication. No deployment/automatic enablement is authorized by this result.

## Unedited CHECKPOINT lines
CHECKPOINT CP-21 commit=185e87f5e9fae7571186eace4961d35e29d7e9c6 build=failed filter=/*/*/RunnerSeatOrphanSweepTests*/(Discovery_request_uses_runner_owned_delivery_evidence*)|(Server_restart_reacquires_runner_delivery_evidence*)|(Evidence_missing_or_peer_unsupported_defers_discovery*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=185e87f5e9fae7571186eace4961d35e29d7e9c6 sourceState=clean buildSource=unknown
CHECKPOINT CP-21 commit=745ea65a7c82198df63793952700769ff6657c82 build=ok filter=/*/*/RunnerSeatOrphanSweepTests*/(Discovery_request_uses_runner_owned_delivery_evidence*)|(Server_restart_reacquires_runner_delivery_evidence*)|(Evidence_missing_or_peer_unsupported_defers_discovery*) executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-bfc79905/.antiphon/checkpoints/20261005-154716-35be/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=745ea65a7c82198df63793952700769ff6657c82 sourceState=clean buildSource=verified
CHECKPOINT CP-21 commit=3087478c04e68fd445e8c64366769f7fb2a58ab4 build=ok filter=/*/*/RunnerSeatOrphanSweepTests*/(Discovery_request_uses_runner_owned_delivery_evidence*)|(Server_restart_reacquires_runner_delivery_evidence*)|(Evidence_missing_or_peer_unsupported_defers_discovery*) executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-bfc79905/.antiphon/checkpoints/20261005-155334-14be/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=3087478c04e68fd445e8c64366769f7fb2a58ab4 sourceState=clean buildSource=verified
CHECKPOINT CP-21 commit=ca9ff7f3dc03653a4eefc2a29c51c03356a6982b build=ok filter=/*/*/RunnerSeatOrphanSweepTests*/(Discovery_request_uses_runner_owned_delivery_evidence*)|(Server_restart_reacquires_runner_delivery_evidence*)|(Evidence_missing_or_peer_unsupported_defers_discovery*) executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-bfc79905/.antiphon/checkpoints/20261005-155924-f90b/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=ca9ff7f3dc03653a4eefc2a29c51c03356a6982b sourceState=clean buildSource=verified
