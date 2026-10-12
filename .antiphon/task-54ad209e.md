# CARD-1205 repair 4 Code report

Implemented the common held-event preservation exit; F1 is reproduced on unchanged
base production and the required ordinary scope is green (119/119).

Task / landing owner: 54ad209e-df48-4ac1-9fe8-e05fd928c3ba.
Original S1/S2 Code task: e07d1562-19e1-4f10-bb19-b2153bb47636; original branch
feat/card-task-e07d1562; inherited S1 34df9193c lands together with S2.
This repair becomes the landing owner as directed by its brief.
Branch: feat/card-task-54ad209e.
Worktree: /work/worktrees/task-54ad209e.
Task base: 115b4fbef85cb493e06cc0e36dd2d711124c9dfa. Final tested implementation: c006ffd2f715f924db42a955a3b0aaea1ba09090.
Test-first commit: b68aadec0603152bafbab1faf95d4111eca42fe4 (pushed before baseline).
Fix commit: c006ffd2f715f924db42a955a3b0aaea1ba09090 (pushed before ordinary verification).
The final report slice changes only this report and plan wording/PC binding;
no production/test behavior changed after the green tested commit. Later receipt
facts are not relabelled as verification of a later report commit.

## Change and invariant

SessionRunnerEventPump handles each readiness marker in one try/finally. Every
catch-up exit, including cancellation inside the ordinary list-error log and final
pull, offers the marker-held payload through PreserveHeldEventAsync exactly once.
Only preservation decides completed versus explicit abandonment. Successful offers
suppress the HTTP iterator next copy; uncertain/abandoned offers unwind without
replay. List throw without cancellation retains its original connection behavior.
The exit-path code comment documents success, timeout, error, cancellation, callback
failure/pending, non-cooperative list, and subsequent iterator fault/disposal.

The C2 list wait independently uses the existing ListTimeoutSeconds bound with
injected monotonic time and host cancellation. A non-cooperative request retains
its own scope and has a late fault/disposal observer. Held delivery keeps the
existing three-second linked-token rule and initial uncancelled offer. Its separate
scope remains owned until callback completion. No timeout was widened and no existing
assertion was weakened. The original HTTP marker/parser contract and T01 are unchanged.
A callback/request that ignores cancellation may remain alive until it settles;
this bounds asynchronous waiting, not synchronous callback code or forced termination.
There is no output replay queue or unconditional receipt/late-ordering guarantee.
Completed held offers preserve in-connection ordering. Later ordinary callbacks
retain their existing dispatch behavior.

Changed files in this repair: pump; DTO marker comment; new held-exit matrix class;
main plan; test design; new repair4 checkpoint manifest; this individual report.
All assigned slices were committed/pushed on the authorized fast-forward branch.
No extra approval was needed; no human decision remains.

## Tests and baseline red

The table-driven test contains 48 literal Arguments rows: 4 second-list outcomes x
6 cancellation schedules x 2 consumer behaviors. Every row explicitly carries
accepted=1/abandoned=0 (responsive) or accepted=0/abandoned=1 (blocked), and asserts
one attempt, original text where accepted, iterator disposal and pump completion.
Blocked requests/callbacks are gate controlled, use fake monotonic time, and are
released/joined on green and red teardown. T52 adds actual iterator read/disposal
fault controls. There are no sleeps or fabricated/self-comparison outcomes.
A success list has no error handler; failed/pending lists have no final pull. Those
unreachable Cartesian phases explicitly cancel at the final held boundary instead.
The F1 row uses the actual caught-list-error handler; success/final-pull uses the
actual runtime pull. The fixture/manifest disclose the substitution.

Production diff between 115b4fbef85cb493e06cc0e36dd2d711124c9dfa and the test-first commit is empty.
Baseline RED-1 at b68aadec0603152bafbab1faf95d4111eca42fe4 executed all 50 cases:
28 passed, 22 assertion failures, 0 skipped/build/fixture failures. F1
throws/error-handler/responsive failed Bus.Accepted expected 1, actual 0. Two other
responsive held-boundary cancellations also accepted zero. Seven blocked success/
throw rows failed iterator disposal before release. All twelve never-returning
list rows failed the initial-offer assertion. Both iterator controls passed.
All these rows were subsequently rerun in CP-2 and passed; none was waved away as a
flake and no deliberate mutant ran. The full baseline/fix row comparison follows.

## Ordinary scope and provenance

Explicit repair brief and repair4 manifest override the generic Final Unit profile:
no whole Unit, namespace or assembly run. CP-1 2/2; CP-2 84/84; ADJ-1 33/33;
119 executions, 117 distinct cases, zero failed/skipped. Full affected classes:
TurnEndGapReproductionTests 2; SessionRunnerStreamReadinessTests 23;
SessionRunnerEventPumpTests 5; SessionRunnerHeldEventDeliveryTests 4;
SessionRunnerHeldEventExitMatrixTests 50. Adjacent full classes:
SessionRunnerGenerationWireTests 9; SessionRunnerHttpClientHostStatsTests 3;
SessionRunnerSettingsTests 1; SessionStateCommitTests 8;
TranscriptWorkingStateQueryTests 12. Fresh TRX confirms each intended class/method
and all 48 literal matrix argument names; 119 unique ordinary execution IDs.
T01 passes in both CP-1 and CP-2 with its original preGraceEndCount.ShouldBe(1).

All run/start/wait processes completed and were awaited to final exit (baseline 1,
ordinary 0), with no orphaned owned child. Runs use the checkpoint tool, --serial,
full expected-source-sha, one actual isolated build per committed group,
UseAppHost=false and forward-slash OutputPath. Each manifest has two declared build
IDs but executes only the group required one (the other is unused). Baseline build:
slot=granted waited=15s; baseline row slot=granted waited=45s. Ordinary build:
slot=granted waited=0s; CP-1 waited=0s, CP-2 waited=15s, ADJ-1 waited=30s; every row
slot=granted. Both test builds had 700 warnings/0 errors, no warning naming a changed
source file. Source snapshots for baseline and ordinary are clean, dirty=0,
matching exact expected/tested SHA, buildSource=verified.
validate-checkpoint-receipt.ps1 passed for all three ordinary rows at c006ffd2f715f924db42a955a3b0aaea1ba09090.
Baseline clean source/provenance is separately recorded despite intended red outcomes.
No repeat proof or loaded repetition was needed after green.

Only unlisted driver: checkpoint-tool setup bootstrap (no tool binary existed),
through scripts/build-slot.ps1, label c1205-r4-tool-bootstrap,
lease f195652b-eedb-4344-a42f-d4a8a2079fe3, slot=granted waited=0s held=6s,
OutputPath bin-c1205-r4-tool/, UseAppHost=false, 1 existing nullable warning/0 errors.
No other unlisted build/test driver ran. Checkpoint wall: red 3m58s, ordinary 7m48s.
Both executors are dead and tool shadow outputs were cleaned by wait. The green
tool removed both task-owned test output names; the separately owned bootstrap
output was removed after realpath containment/producer checks. A recursive owned-name
inventory found no bin-c1205-r4-* directories. Generated TRX/JSON/logs remain ignored
in the original checkpoint directories; no force-add or payload relocation occurred.

GET /api/runner-defaults and /api/session-runners succeeded with task-bound reads.
No fleet location or runner/platform pin was introduced. No server/runner restart
or live activation was performed or required. Restart: none.

## V/R actual outcomes
V-1 : Passed CP-1 and CP-2
V-2 : Passed CP-1 and CP-2
V-3 : Passed CP-2
V-4 : Passed CP-2
V-5 : Passed CP-2
V-6 : Passed CP-2
V-7 : Passed CP-2
V-8 : Not run; deferred to S3-S5, outside repair scope
V-9 : Not run; deferred to S3-S5, outside repair scope
V-10 : Not run; deferred to S3-S5, outside repair scope
V-11 : Not run; deferred to S3-S5, outside repair scope
V-12 : Not run; deferred to S3-S5, outside repair scope
V-13 : Not run; deferred to S3-S5, outside repair scope
V-14 : Not run; deferred to S3-S5, outside repair scope
V-15 : Not run; deferred to S3-S5, outside repair scope
V-16 : Not run; deferred to S3-S5, outside repair scope
V-17 : Not run; deferred to S3-S5, outside repair scope
V-18 : Not run; deferred to S3-S5, outside repair scope
V-19 : Not run; deferred to S3-S5, outside repair scope
V-20 : Not run; deferred to S3-S5, outside repair scope
V-21 : Not run; deferred to S3-S5, outside repair scope
V-22 : Not run; deferred to S3-S5, outside repair scope
V-23 : Not run; deferred to S3-S5, outside repair scope
V-24 : Not run; deferred to S3-S5, outside repair scope
V-25 : Not run; deferred to S3-S5, outside repair scope
V-26 : Not run; deferred to S3-S5, outside repair scope
V-27 : Not run; deferred to S3-S5, outside repair scope
V-28 : Not run; deferred to S3-S5, outside repair scope
V-29 : Not run; deferred to S3-S5, outside repair scope
V-30 : Not run; deferred to S3-S5, outside repair scope
V-31 : Not run; deferred to S3-S5, outside repair scope
V-32 : Not run; deferred to S3-S5, outside repair scope
V-33 : Not run; deferred to S3-S5, outside repair scope
V-34 : Not run; deferred to S3-S5, outside repair scope
V-35 : Not run; deferred to S3-S5, outside repair scope
V-36 : Not run; deferred to S3-S5, outside repair scope
V-37 : Not run; deferred to S3-S5, outside repair scope
V-38 : Not run; deferred to S3-S5, outside repair scope
V-39 : Not run; deferred to S3-S5, outside repair scope
V-40 : Not run; deferred to S3-S5, outside repair scope

R-1 : Passed CP-1 and CP-2
R-2 : Not run; deferred to S3-S5, outside repair scope
R-3 : Not run; deferred to S3-S5, outside repair scope
R-4 : Not run; deferred to S3-S5, outside repair scope
R-5 : Not run; deferred to S3-S5, outside repair scope
R-6 : Passed CP-2, all five pump cases
R-7 : Passed ADJ-1 adjacent compatibility (8 commit + 12 Working); CP-3 still deferred
R-8 : Not run; deferred to S3-S5, outside repair scope
R-9 : Not run; deferred to S3-S5, outside repair scope

No required manual acceptance was commissioned for S1/S2. S3 checked recovery,
S4 watchdog and S5 actual producer-to-recipient/restart-survival acceptance remain
outside scope. No native/live AppHost/PTY receipt guarantee is claimed here.

## Mutation handoff

Post-land Mutation was NOT run. PC-1..PC-171 and EVERY parameter/variant are pending
for method-scoped SourceLanding Mutation, including inherited PC-1..119 and the
matrix/control PC-120..171. Matrix case -> payload -> decisive red is enumerated
in test design. PC-118 is rebound for the common finally; its old cancellation-check
removal alone is redundant. Shared matrix payloads may supply per-row red evidence
without repeating unchanged proof. Mutation owns deliberate break/red/restore/green
and missing-control discovery. No bound pack was created in Code; the unbound
candidate is the test design, eventual packs belong under docs/superpowers/mutations/.
Ordinary green does not discharge a PC. Caller must commission Review next, then
land this original Code task and commission SourceLanding Mutation.

## Unedited checkpoint reports

~~~~text--- checkpoint report ---
run: 20261012-223322-8f3d   manifest: /work/worktrees/task-54ad209e/.antiphon/checkpoints/20261012-223322-8f3d/manifest.resolved.yaml
commit: b68aadec0603152bafbab1faf95d4111eca42fe4  branch: feat/card-task-54ad209e  worktree: /work/worktrees/task-54ad209e  host: Debian GNU/Linux 12 (bookworm) cores=24
source: b68aadec0603152bafbab1faf95d4111eca42fe4 state=clean buildSource=verified
CHECKPOINT RED-1 commit=b68aadec0603152bafbab1faf95d4111eca42fe4 build=ok filter=/*/*/SessionRunnerHeldEventExitMatrixTests*/* executed=50 passed=28 failed=22 skipped=0 trx=/work/worktrees/task-54ad209e/.antiphon/checkpoints/20261012-223322-8f3d/rows/RED-1/run.trx slot=granted waited=45s dirty=0 source=b68aadec0603152bafbab1faf95d4111eca42fe4 sourceState=clean buildSource=verified
PHASES RED-1 slotWait=60s build=161.3022298s startup=9.4840113s testsWall=4.4098135s teardown=0.909839s hostWall=14.8036819s
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: h.Bus.Accepted     should be 1     but was 0 -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: h.Bus.Accepted     should be 1     but was 0 -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: h.Bus.Accepted     should be 1     but was 0 -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: offered     should be True     but was False  Additional Info:     every held exit must offer the original output -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: offered     should be True     but was False  Additional Info:     every held exit must offer the original output -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: offered     should be True     but was False  Additional Info:     every held exit must offer the original output -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: offered     should be True     but was False  Additional Info:     every held exit must offer the original output -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: disposed     should be True     but was False  Additional Info:     blocked delivery must unwind the iterator before fixture release -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: disposed     should be True     but was False  Additional Info:     blocked delivery must unwind the iterator before fixture release -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: disposed     should be True     but was False  Additional Info:     blocked delivery must unwind the iterator before fixture release -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: disposed     should be True     but was False  Additional Info:     blocked delivery must unwind the iterator before fixture release -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: disposed     should be True     but was False  Additional Info:     blocked delivery must unwind the iterator before fixture release -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: disposed     should be True     but was False  Additional Info:     blocked delivery must unwind the iterator before fixture release -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: disposed     should be True     but was False  Additional Info:     blocked delivery must unwind the iterator before fixture release -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: offered     should be True     but was False  Additional Info:     every held exit must offer the original output -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: offered     should be True     but was False  Additional Info:     every held exit must offer the original output -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: offered     should be True     but was False  Additional Info:     every held exit must offer the original output -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: offered     should be True     but was False  Additional Info:     every held exit must offer the original output -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: offered     should be True     but was False  Additional Info:     every held exit must offer the original output -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: offered     should be True     but was False  Additional Info:     every held exit must offer the original output -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: offered     should be True     but was False  Additional Info:     every held exit must offer the original output -> rows/RED-1/failures.md
FAILED Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests.C1205_Every_held_exit_has_an_explicit_outcome (RED-1) ShouldAssertException: offered     should be True     but was False  Additional Info:     every held exit must offer the original output -> rows/RED-1/failures.md
SLOW CLASS Antiphon.Tests.Application.SessionRunnerHeldEventExitMatrixTests 65s tests=50 (RED-1)
unlisted: none (the tool ran no other build or test command)
wall: 3m58s  sequential-equivalent: 1m00s  builds: 2  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
outputs: kept bin-c1205-r4-red/, bin-c1205-r4-green/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261012-223322-8f3d
evidence: /work/worktrees/task-54ad209e/.antiphon/checkpoints/20261012-223322-8f3d/report.md
verdict: RED exit=1

~~~~

~~~~text
--- checkpoint report ---
run: 20261012-224059-d2e6   manifest: /work/worktrees/task-54ad209e/.antiphon/checkpoints/20261012-224059-d2e6/manifest.resolved.yaml
commit: c006ffd2f715f924db42a955a3b0aaea1ba09090  branch: feat/card-task-54ad209e  worktree: /work/worktrees/task-54ad209e  host: Debian GNU/Linux 12 (bookworm) cores=24
source: c006ffd2f715f924db42a955a3b0aaea1ba09090 state=clean buildSource=verified
CHECKPOINT CP-1 commit=c006ffd2f715f924db42a955a3b0aaea1ba09090 build=ok filter=/*/Antiphon.Tests.Application/TurnEndGapReproductionTests*/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-54ad209e/.antiphon/checkpoints/20261012-224059-d2e6/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=c006ffd2f715f924db42a955a3b0aaea1ba09090 sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=197.3227107s startup=45.5813042s testsWall=12.0490562s teardown=1.3051527s hostWall=58.9592828s
CHECKPOINT CP-2 commit=c006ffd2f715f924db42a955a3b0aaea1ba09090 build=reused filter=/*/Antiphon.Tests.Application/(TurnEndGapReproductionTests*)|(SessionRunnerStreamReadinessTests*)|(SessionRunnerEventPumpTests*)|(SessionRunnerHeldEventDeliveryTests*)|(SessionRunnerHeldEventExitMatrixTests*)/* executed=84 passed=84 failed=0 skipped=0 trx=/work/worktrees/task-54ad209e/.antiphon/checkpoints/20261012-224059-d2e6/rows/CP-2/run.trx slot=granted waited=15s dirty=0 source=c006ffd2f715f924db42a955a3b0aaea1ba09090 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=15s build=0s startup=59.8008398s testsWall=48.4639411s teardown=1.7233445s hostWall=109.9881251s
SLOW CLASS Antiphon.Tests.Application.SessionRunnerStreamReadinessTests 95s tests=23 (CP-2)
CHECKPOINT ADJ-1 commit=c006ffd2f715f924db42a955a3b0aaea1ba09090 build=reused filter=/*/*/(SessionRunnerHttpClientHostStatsTests*)|(SessionRunnerGenerationWireTests*)|(SessionRunnerSettingsTests*)|(SessionStateCommitTests*)|(TranscriptWorkingStateQueryTests*)/* executed=33 passed=33 failed=0 skipped=0 trx=/work/worktrees/task-54ad209e/.antiphon/checkpoints/20261012-224059-d2e6/rows/ADJ-1/run.trx slot=granted waited=30s dirty=0 source=c006ffd2f715f924db42a955a3b0aaea1ba09090 sourceState=clean buildSource=verified
PHASES ADJ-1 slotWait=30s build=0s startup=37.4955186s testsWall=13.0017016s teardown=2.2653386s hostWall=52.7625586s
SLOW CLASS Antiphon.Tests.Application.SessionStateCommitTests 102s tests=8 (ADJ-1)
SLOW CLASS Antiphon.Tests.Application.TranscriptWorkingStateQueryTests 74s tests=12 (ADJ-1)
unlisted: none (the tool ran no other build or test command)
wall: 7m48s  sequential-equivalent: 4m29s  builds: 2  max-concurrent-builds: 1  rows: 3 green 0 red 0 skipped
outputs: deleted bin-c1205-r4-red/, bin-c1205-r4-green/
evidence: /work/worktrees/task-54ad209e/.antiphon/checkpoints/20261012-224059-d2e6/report.md
verdict: GREEN exit=0

~~~~

## Matrix and iterator baseline/fix outcomes

| Expanded case | Baseline | Fixed |
|---|---|---|
| C1205_Every_held_exit_has_an_explicit_outcome(never, before-list, False, 1, 0) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(never, before-list, True, 0, 1) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(never, callback, False, 1, 0) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(never, callback, True, 0, 1) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(never, during-list, False, 1, 0) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(never, during-list, True, 0, 1) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(never, error-handler, False, 1, 0) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(never, error-handler, True, 0, 1) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(never, final-pull, False, 1, 0) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(never, final-pull, True, 0, 1) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(never, none, False, 1, 0) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(never, none, True, 0, 1) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(success, before-list, False, 1, 0) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(success, before-list, True, 0, 1) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(success, callback, False, 1, 0) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(success, callback, True, 0, 1) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(success, during-list, False, 1, 0) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(success, during-list, True, 0, 1) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(success, error-handler, False, 1, 0) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(success, error-handler, True, 0, 1) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(success, final-pull, False, 1, 0) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(success, final-pull, True, 0, 1) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(success, none, False, 1, 0) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(success, none, True, 0, 1) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(throws, before-list, False, 1, 0) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(throws, before-list, True, 0, 1) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(throws, callback, False, 1, 0) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(throws, callback, True, 0, 1) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(throws, during-list, False, 1, 0) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(throws, during-list, True, 0, 1) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(throws, error-handler, False, 1, 0) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(throws, error-handler, True, 0, 1) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(throws, final-pull, False, 1, 0) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(throws, final-pull, True, 0, 1) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(throws, none, False, 1, 0) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(throws, none, True, 0, 1) | Failed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(timeout, before-list, False, 1, 0) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(timeout, before-list, True, 0, 1) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(timeout, callback, False, 1, 0) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(timeout, callback, True, 0, 1) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(timeout, during-list, False, 1, 0) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(timeout, during-list, True, 0, 1) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(timeout, error-handler, False, 1, 0) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(timeout, error-handler, True, 0, 1) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(timeout, final-pull, False, 1, 0) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(timeout, final-pull, True, 0, 1) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(timeout, none, False, 1, 0) | Passed | Passed |
| C1205_Every_held_exit_has_an_explicit_outcome(timeout, none, True, 0, 1) | Passed | Passed |
| C1205_Iterator_failure_after_preservation_does_not_redeliver(dispose-fault) | Passed | Passed |
| C1205_Iterator_failure_after_preservation_does_not_redeliver(read-fault) | Passed | Passed |

## Rerun and review artifacts

Manifest: docs/superpowers/plans/2026-10-12-card-1205-s2-repair4-checkpoints.md.
Plan: docs/superpowers/plans/2026-10-12-card-1205-turnend-gap-plan.md.
Test design: docs/superpowers/plans/2026-10-12-card-1205-test-design.md.
Evidence roots (absolute):
/work/worktrees/task-54ad209e/.antiphon/checkpoints/20261012-223322-8f3d
/work/worktrees/task-54ad209e/.antiphon/checkpoints/20261012-224059-d2e6
This report: /work/worktrees/task-54ad209e/.antiphon/task-54ad209e.md.

Bootstrap through build-slot.ps1, then run the checkpoint tool with --plan the
repair4 manifest --after RepairFix --serial --expected-source-sha full committed
HEAD. Wait until exit is not 75. No broad Unit lane is commissioned by this repair.
Receipt validation uses report.json and its actual tested SHA, never a later HEAD.
The main plan/test design/current comments were searched for overstated delivery
guarantees; successful held offers and bounded abandonment are distinguished, and
ordering after late completion/reconnect is explicitly limited. The final doc-only
slice clarifies that limit and aligns the legacy PC-118 table with its revised binding.

Merge-tree of tested candidate against fetched origin/master
4f252abf4a5f41057cf871d04bc93cf8da96640a exited 0,
result tree 02f14c4ce4a50b5d628ce9764432394742c91993.
Final pushed SHA, final merge-tree and full base..HEAD evidence-diff results are
reported again in settlement. The preliminary full repair range guard through
c006ffd2f715f924db42a955a3b0aaea1ba09090 passed: 2 commits, 0 policy violations. Final report-slice guard is required
before settlement. Git diff --check passed before the implementation commit.

--- next stage ---
next: review
handoff: Review common-finally held preservation, 22 clean baseline assertion reds (including F1), 119/119 ordinary green at c006ffd2f715f924db42a955a3b0aaea1ba09090, and the explicit unreachable-phase substitutions. S3-S5 deferred; PC-1..171/variants pending SourceLanding Mutation. Landing owner 54ad209e. Restart none.
artifact: docs/superpowers/plans/2026-10-12-card-1205-s2-repair4-checkpoints.md

[antiphon-report:54ad209e done]